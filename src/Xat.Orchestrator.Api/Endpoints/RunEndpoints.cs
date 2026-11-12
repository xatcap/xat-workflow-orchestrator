using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Api.Contracts.Runs;
using Xat.Orchestrator.Api.Errors;
using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Application.Jobs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>The routes that run a pipeline. The routes are thin: they read the request, start the run and write the result.</summary>
internal static class RunEndpoints
{
    /// <summary>Maps the run routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/pipelines/{name}/runs", (string name, HttpContext http, RunStarter starter, AsyncRunService runs) =>
                RunAsync(name, http, starter, runs))
            .RequireRateLimiting(RunRules.RateLimitPolicy);

        app.MapPost("/v1/ask", (HttpContext http, IOptions<ApiOptions> options, RunStarter starter, AsyncRunService runs) =>
                RunAsync(options.Value.DefaultPipeline, http, starter, runs))
            .RequireRateLimiting(RunRules.RateLimitPolicy);

        return app;
    }

    private static async Task RunAsync(string name, HttpContext http, RunStarter starter, AsyncRunService runs)
    {
        var ct = http.RequestAborted;
        var pipeline = starter.Find(name);
        if (!pipeline.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, pipeline.Error, null, ct);
            return;
        }

        var body = await ReadBodyAsync(http, ct);
        if (!body.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, body.Error, null, ct);
            return;
        }

        var request = body.Value;
        var mode = http.Request.Query["mode"].ToString();
        if (mode.Length > 0 && mode is not ("sync" or "async"))
        {
            await ApiErrors.WriteAsync(
                http,
                new PipelineError("request.mode_invalid", "The query parameter mode must be sync or async.", ErrorKind.Validation),
                null,
                ct);
            return;
        }

        if (mode == "async")
        {
            await SubmitAsync(http, runs, pipeline.Value, request, ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.Webhook))
        {
            await ApiErrors.WriteAsync(
                http,
                new PipelineError("webhook.requires_async", "A webhook needs the query parameter mode=async.", ErrorKind.Validation),
                null,
                ct);
            return;
        }

        var input = request.Input ?? JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

        // The run has its own token. The stream loop cancels it when the loop ends for any reason, so a failed write
        // never leaves a run that goes on and holds the model while nobody reads its events.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var started = starter.Start(pipeline.Value, input, request.Stream, runCts.Token);
        if (!started.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, started.Error, null, ct);
            return;
        }

        var handle = started.Value;
        http.Response.Headers["X-Run-Id"] = handle.RunId;

        if (request.Stream)
        {
            try
            {
                await WriteStreamAsync(http, handle, request.Debug, ct);
            }
            finally
            {
                await runCts.CancelAsync();
            }

            return;
        }

        var result = await handle.Completion;
        if (!result.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, result.Error, handle.RunId, ct);
            return;
        }

        await http.Response.WriteAsJsonAsync(RunResponseDto.From(result.Value, request.Debug), ApiJson.Options, "application/json", ct);
    }

    /// <summary>Accepts an asynchronous run. The answer is 202 with the ID of the run and the path of its state.</summary>
    private static async Task SubmitAsync(
        HttpContext http, AsyncRunService runs, PipelineDefinition pipeline, RunRequestDto request, CancellationToken ct)
    {
        var input = request.Input ?? JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
        var accepted = runs.Submit(pipeline, input, request.Webhook, request.Debug, request.Stream);
        if (!accepted.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, accepted.Error, null, ct);
            return;
        }

        var statusUrl = $"/v1/runs/{accepted.Value.RunId}";
        http.Response.StatusCode = StatusCodes.Status202Accepted;
        http.Response.Headers.Location = statusUrl;
        http.Response.Headers["X-Run-Id"] = accepted.Value.RunId;
        await http.Response.WriteAsJsonAsync(
            new AcceptedRunDto(accepted.Value.RunId, "queued", statusUrl), ApiJson.Options, "application/json", ct);
    }

    private static async Task<Result<RunRequestDto>> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasJsonContentType())
        {
            return Result<RunRequestDto>.Fail(new PipelineError(
                "request.content_type", "The content type must be application/json.", ErrorKind.Validation));
        }

        if (http.Request.ContentLength == 0)
        {
            return Result<RunRequestDto>.Fail(new PipelineError("request.empty", "The request body is empty.", ErrorKind.Validation));
        }

        try
        {
            var body = await http.Request.ReadFromJsonAsync<RunRequestDto>(ApiJson.Options, ct);
            return body is null
                ? Result<RunRequestDto>.Fail(new PipelineError("request.empty", "The request body is empty.", ErrorKind.Validation))
                : Result<RunRequestDto>.Ok(body);
        }
        catch (JsonException)
        {
            return Result<RunRequestDto>.Fail(new PipelineError(
                "request.invalid_json", "The request body is not valid JSON of the expected form.", ErrorKind.Validation));
        }
    }

    private static async Task WriteStreamAsync(HttpContext http, RunHandle handle, bool debug, CancellationToken ct)
    {
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        var ended = false;
        try
        {
            await foreach (var runEvent in handle.Events!.ReadAllAsync(ct))
            {
                ended |= runEvent is RunEvent.Completed or RunEvent.Failed;
                var (name, payload) = ToWire(runEvent, debug);
                await WriteEventAsync(http, name, payload, ct);
            }

            // The channel can close with no last event, for example when a slow reader makes the run give up on it.
            // The caller must never see this as a normal end, so the route sends an error event.
            if (!ended)
            {
                var lost = new PipelineError("run.stream_lost", "The stream ended before the answer was complete.", ErrorKind.Internal);
                await WriteEventAsync(http, "run.error", ApiErrors.ToEnvelope(lost, handle.RunId), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The caller disconnected. The run sees the same token and stops, so there is nothing more to write.
        }
    }

    private static async Task WriteEventAsync(HttpContext http, string name, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {name}\ndata: {ApiErrors.Serialize(payload)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static (string Name, object Payload) ToWire(RunEvent runEvent, bool debug) =>
        runEvent switch
        {
            RunEvent.Started started => ("run.started", new { started.RunId, started.Pipeline, started.Version }),
            RunEvent.StepStarted step => ("step.started", new { StepId = step.StepId, step.Uses }),
            RunEvent.Delta delta => ("answer.delta", new { delta.Text }),
            RunEvent.Citations citations => ("answer.citations", new { Citations = citations.Items.Select(CitationDto.From).ToList() }),
            RunEvent.Completed completed => ("run.completed", RunResponseDto.From(completed.Result, debug)),
            RunEvent.Failed failed => ("run.error", ApiErrors.ToEnvelope(failed.Error, failed.RunId)),
            _ => ("run.unknown", new { }),
        };
}
