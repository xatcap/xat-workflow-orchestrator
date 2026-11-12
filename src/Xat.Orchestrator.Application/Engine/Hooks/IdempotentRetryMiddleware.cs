using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Engine.Hooks;

/// <summary>
/// Runs an idempotent step again after an upstream failure. The HTTP clients already retry a refused connection, 429, 503 and 504,
/// so this middleware retries only <see cref="ErrorKind.UpstreamFailed"/>: an other 5xx status, an answer that cannot be read,
/// or a body that stopped in the middle. It never retries a step that already sent an event to the caller.
/// </summary>
internal sealed class IdempotentRetryMiddleware(
    IStepRegistry registry,
    IOptions<RunnerOptions> options,
    ILogger<IdempotentRetryMiddleware> logger) : IStepMiddleware
{
    private static readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan _maxJitter = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc/>
    public async ValueTask<StepOutcome> InvokeAsync(StepContext context, StepInvocation nextStep, CancellationToken ct)
    {
        var retries = options.Value.IdempotentRetryCount;
        var idempotent = registry.Find(context.Step.Uses)?.Traits.HasFlag(StepTraits.Idempotent) == true;
        if (retries == 0 || !idempotent)
        {
            return await nextStep(ct);
        }

        for (var attempt = 0; ; attempt++)
        {
            var emittedBefore = context.Run.Events.EmittedCount;
            var outcome = await nextStep(ct);
            var delay = _baseDelay + (_maxJitter * Random.Shared.NextDouble());
            var canRetry = attempt < retries
                && outcome is StepOutcome.FailedOutcome { Error.Kind: ErrorKind.UpstreamFailed }
                && context.Run.Events.EmittedCount == emittedBefore
                && !ct.IsCancellationRequested
                && context.Run.Remaining() > delay;
            if (!canRetry)
            {
                return outcome;
            }

            var code = ((StepOutcome.FailedOutcome)outcome).Error.Code;
            RunnerLog.StepRetried(logger, context.Run.RunId, context.Step.Id, code, attempt + 1);
            await Task.Delay(delay, context.Run.Clock, ct);
        }
    }
}
