using System.Text.Json;
using Xat.Orchestrator.Api.Endpoints;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api.OpenAi;

/// <summary>
/// The OpenAI-compatible route <c>POST /v1/chat/completions</c>. An OpenAI client library can call a pipeline with the model
/// <c>pipeline/&lt;name&gt;</c>. The route runs the same engine as the other routes. The orchestrator has no authentication,
/// so the route accepts any API key and ignores the <c>Authorization</c> header.
/// </summary>
internal static class ChatCompletionsEndpoint
{
    /// <summary>Maps the route.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapChatCompletionsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", (HttpContext http) => HandleAsync(http))
            .RequireRateLimiting(RunRules.RateLimitPolicy);
        return app;
    }

    private static async Task HandleAsync(HttpContext http)
    {
        var ct = http.RequestAborted;

        if (!http.Request.HasJsonContentType())
        {
            await WriteErrorAsync(http, new PipelineError("request.content_type", "The content type must be application/json.", ErrorKind.Validation), ct);
            return;
        }

        JsonElement body;
        try
        {
            using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            await WriteErrorAsync(http, new PipelineError("request.invalid_json", "The request body is not valid JSON.", ErrorKind.Validation), ct);
            return;
        }

        var catalog = http.RequestServices.GetRequiredService<IPipelineCatalog>();
        var call = ChatCompletionRequestReader.Read(body, catalog);
        if (!call.IsSuccess)
        {
            await WriteErrorAsync(http, call.Error, ct);
            return;
        }

        var streamError = RunRules.CheckStreamMode(call.Value.Pipeline, call.Value.Stream);
        if (streamError is not null)
        {
            await WriteErrorAsync(http, streamError, ct);
            return;
        }

        // The run has its own token, and the end of this method cancels it, so a failed write never leaves a run that nobody reads.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runner = http.RequestServices.GetRequiredService<PipelineRunner>();
        var created = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        var handle = runner.Start(call.Value.Pipeline, call.Value.Input, new RunRequest(call.Value.Stream), runCts.Token);
        http.Response.Headers["X-Run-Id"] = handle.RunId;
        try
        {
            if (call.Value.Stream)
            {
                await StreamAsync(http, handle, call.Value, created, ct);
                return;
            }

            var result = await handle.Completion;
            if (!result.IsSuccess)
            {
                await WriteErrorAsync(http, result.Error, ct);
                return;
            }

            var json = ChatCompletionJson.Completion(call.Value.Model, created, result.Value, call.Value.Warnings, call.Value.Debug);
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync(json.ToJsonString(), System.Text.Encoding.UTF8, ct);
        }
        finally
        {
            await runCts.CancelAsync();
        }
    }

    /// <summary>
    /// Sends the answer as a stream. Like xat-llm-engine, the route sends the status 200 only when the first piece is ready.
    /// An error before that piece is a normal HTTP error. An error after it is an event with the body <c>{"error":{...}}</c>, and the stream has no end marker.
    /// </summary>
    private static async Task StreamAsync(HttpContext http, RunHandle handle, ChatCompletionCall call, long created, CancellationToken ct)
    {
        var id = ChatCompletionJson.Id(handle.RunId);
        var committed = false;
        var ended = false;

        async Task CommitAsync()
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            committed = true;
            await WriteDataAsync(http, ChatCompletionJson.Chunk(id, call.Model, created, role: "assistant", content: string.Empty, finishReason: null).ToJsonString(), ct);
        }

        try
        {
            await foreach (var runEvent in handle.Events!.ReadAllAsync(ct))
            {
                switch (runEvent)
                {
                    case RunEvent.Delta delta:
                        if (!committed)
                        {
                            await CommitAsync();
                        }

                        await WriteDataAsync(http, ChatCompletionJson.Chunk(id, call.Model, created, null, delta.Text, null).ToJsonString(), ct);
                        break;

                    case RunEvent.Completed completed:
                        ended = true;
                        if (!committed)
                        {
                            // A step ended the run without streaming, for example the no-answer path. The text goes out as one piece.
                            await CommitAsync();
                            await WriteDataAsync(http, ChatCompletionJson.Chunk(id, call.Model, created, null, completed.Result.Answer.Text, null).ToJsonString(), ct);
                        }

                        var extension = ChatCompletionJson.Extension(completed.Result, call.Warnings, call.Debug);
                        await WriteDataAsync(http, ChatCompletionJson.Chunk(id, call.Model, created, null, null, "stop", extension).ToJsonString(), ct);
                        if (call.IncludeUsage)
                        {
                            await WriteDataAsync(http, ChatCompletionJson.UsageChunk(id, call.Model, created, completed.Result).ToJsonString(), ct);
                        }

                        await WriteDataAsync(http, "[DONE]", ct);
                        break;

                    case RunEvent.Failed failed when !committed:
                        ended = true;
                        await WriteErrorAsync(http, failed.Error, ct);
                        break;

                    case RunEvent.Failed failed:
                        ended = true;
                        await WriteDataAsync(http, ChatCompletionJson.Error(failed.Error).ToJsonString(), ct);
                        break;
                }
            }

            // The channel can close without a last event, for example when a slow reader makes the run give up on it.
            // The caller must never see this as a normal end, so the route reports an error.
            if (!ended)
            {
                var lost = new PipelineError("run.stream_lost", "The stream ended before the answer was complete.", ErrorKind.Internal);
                if (committed)
                {
                    await WriteDataAsync(http, ChatCompletionJson.Error(lost).ToJsonString(), ct);
                }
                else
                {
                    await WriteErrorAsync(http, lost, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The caller disconnected. The run sees the same token and stops.
        }
    }

    private static async Task WriteDataAsync(HttpContext http, string data, CancellationToken ct)
    {
        await http.Response.WriteAsync($"data: {data}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static Task WriteErrorAsync(HttpContext http, PipelineError error, CancellationToken ct) =>
        ApiErrors.WriteOpenAiAsync(http, error, ct);
}
