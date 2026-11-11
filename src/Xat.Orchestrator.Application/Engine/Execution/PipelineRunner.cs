using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Application.Engine.Hooks;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Engine.Execution;

/// <summary>Executes a pipeline: it runs the steps in order through the middleware, and it handles time limits and cancel.</summary>
public sealed class PipelineRunner(
    IStepRegistry registry,
    IEnumerable<IStepMiddleware> middleware,
    IEnumerable<IPipelineObserver> observers,
    TimeProvider clock,
    IOptions<RunnerOptions> options,
    ILogger<PipelineRunner> logger)
{
    private static readonly TimeSpan _finalEventTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _hookTimeout = TimeSpan.FromSeconds(5);

    private readonly IStepMiddleware[] _middleware = middleware.ToArray();
    private readonly IPipelineObserver[] _observers = observers.ToArray();

    /// <summary>Starts a run. The method returns at once, and the run goes on in the background.</summary>
    /// <param name="definition">The pipeline.</param>
    /// <param name="input">The input of the caller. An undefined value means an empty object.</param>
    /// <param name="request">What the caller asks of the run.</param>
    /// <param name="ct">The token that cancels the run, for example when the caller disconnects.</param>
    /// <returns>The handle of the run.</returns>
    public RunHandle Start(PipelineDefinition definition, JsonElement input, RunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);

        var runId = request.RunId ?? RunIds.New();
        Channel<RunEvent>? channel = null;
        IRunEventSink sink = NullRunEventSink.Instance;
        if (request.Stream)
        {
            channel = Channel.CreateBounded<RunEvent>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            sink = new ChannelRunEventSink(channel.Writer);
        }

        var context = new PipelineContext
        {
            RunId = runId,
            Pipeline = definition,
            State = new PipelineState(
                input.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>()) : input),
            Events = sink,
            Clock = clock,
        };
        var completion = RunAsync(context, channel?.Writer, ct);
        return new RunHandle(runId, channel?.Reader, completion);
    }

    private async Task<Result<RunResult>> RunAsync(PipelineContext context, ChannelWriter<RunEvent>? writer, CancellationToken ct)
    {
        var timings = new List<StepTiming>();
        Result<RunResult> result;
        try
        {
            result = await ExecuteAsync(context, timings, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RunnerLog.UnexpectedFailure(logger, exception, context.RunId);
            result = Result<RunResult>.Fail(new PipelineError(
                "run.internal_error",
                "The run failed because of an internal error.",
                ErrorKind.Internal));
        }

        try
        {
            // The last event must reach the caller, but a caller that is gone must not block the run forever.
            using var timeout = new CancellationTokenSource(_finalEventTimeout, clock);
            RunEvent last = result.IsSuccess
                ? new RunEvent.Completed(result.Value)
                : new RunEvent.Failed(context.RunId, result.Error);
            await context.Events.EmitAsync(last, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The caller is gone. There is nobody to receive the last event.
        }
        finally
        {
            writer?.TryComplete();
        }

        // The completion hooks run for every outcome: success, failure, cancel and timeout.
        // They run after the caller got the last event, and each hook has a time limit.
        await NotifyAsync((observer, token) => observer.OnRunCompletedAsync(context, result, token), CancellationToken.None);
        return result;
    }

    private async Task<Result<RunResult>> ExecuteAsync(PipelineContext context, List<StepTiming> timings, CancellationToken ct)
    {
        var definition = context.Pipeline;
        var state = context.State;
        var timeout = options.Value.RunTimeout;
        using var deadline = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var token = linked.Token;
        var status = RunStatus.Answered;

        try
        {
            await context.Events.EmitAsync(new RunEvent.Started(context.RunId, definition.Name, definition.Version), token);
            await NotifyAsync((observer, hookToken) => observer.OnRunStartedAsync(context, hookToken), token);

            var schemaProblems = InputSchemaValidator.Validate(definition.InputSchema, state.Input);
            if (schemaProblems.Count > 0)
            {
                return Result<RunResult>.Fail(new PipelineError("input.invalid", string.Join(" ", schemaProblems), ErrorKind.Validation));
            }

            foreach (var step in definition.Steps)
            {
                token.ThrowIfCancellationRequested();
                await context.Events.EmitAsync(new RunEvent.StepStarted(step.Id, step.Uses), token);

                var registration = registry.Find(step.Uses);
                if (registration is null)
                {
                    return Result<RunResult>.Fail(new PipelineError(
                        "step.unknown", "The pipeline uses a step type that is not registered.", ErrorKind.Internal, Source: step.Id));
                }

                var stepContext = new StepContext(context, step);
                await NotifyAsync((observer, hookToken) => observer.OnStepStartedAsync(stepContext, hookToken), token);

                var started = clock.GetTimestamp();
                var resolved = ExpressionResolver.Resolve(step.With, state);
                var outcome = resolved.IsSuccess
                    ? await InvokeAsync(registration, stepContext, resolved.Value, token)
                    : StepOutcome.Fail(resolved.Error);
                var elapsed = clock.GetElapsedTime(started);

                timings.Add(new StepTiming(step.Id, step.Uses, elapsed));
                await NotifyAsync((observer, hookToken) => observer.OnStepCompletedAsync(stepContext, outcome, elapsed, hookToken), token);

                if (outcome is StepOutcome.FailedOutcome failed)
                {
                    return Result<RunResult>.Fail(failed.Error with { Source = failed.Error.Source ?? step.Id });
                }

                if (outcome is StepOutcome.CompleteOutcome complete)
                {
                    state.Set(StateKeys.Answer, complete.Answer);
                    status = complete.Status;
                    break;
                }
            }

            if (!state.TryGet(StateKeys.Answer, out var answer))
            {
                return Result<RunResult>.Fail(new PipelineError(
                    "run.no_answer", "The pipeline ended without an answer.", ErrorKind.Internal));
            }

            return Result<RunResult>.Ok(new RunResult(
                context.RunId, definition.Name, definition.Version, status, answer, state.Usage, timings, state.Warnings));
        }
        catch (OperationCanceledException)
        {
            return Result<RunResult>.Fail(ct.IsCancellationRequested
                ? new PipelineError("run.cancelled", "The caller cancelled the run.", ErrorKind.Cancelled)
                : new PipelineError(
                    "run.timeout",
                    $"The run passed its time limit of {timeout.TotalSeconds:0} seconds.",
                    ErrorKind.Timeout));
        }
    }

    private ValueTask<StepOutcome> InvokeAsync(
        IStepRegistration registration,
        StepContext context,
        JsonElement with,
        CancellationToken ct)
    {
        StepInvocation pipeline = token => registration.ExecuteAsync(context, with, token);
        for (var i = _middleware.Length - 1; i >= 0; i--)
        {
            var current = _middleware[i];
            var nextStep = pipeline;
            pipeline = token => current.InvokeAsync(context, nextStep, token);
        }

        return pipeline(ct);
    }

    /// <summary>Calls each hook with its own time limit. A hook that fails or is too slow is logged and ignored.</summary>
    private async Task NotifyAsync(Func<IPipelineObserver, CancellationToken, ValueTask> call, CancellationToken ct)
    {
        foreach (var observer in _observers)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(_hookTimeout);
            try
            {
                await call(observer, limit.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The run itself was cancelled. Let the run handle it.
                throw;
            }
            catch (Exception exception)
            {
                RunnerLog.ObserverFailed(logger, exception, observer.GetType().Name);
            }
        }
    }
}
