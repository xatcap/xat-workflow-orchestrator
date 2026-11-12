using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Infrastructure.XatLlmEngine;

/// <summary>
/// Limits the number of calls that run at the same time for each model, to the <c>Parallel</c> value of the model.
/// The limit is for the model ID, not for the alias, because the engine runs the model. A call that finds the limit full waits
/// in order of arrival. It does not fail. The wait uses the token of the run, so a cancel or the run timeout ends it.
/// The wait comes before the HTTP call, so it does not count against the time limit of the call.
/// A stream holds its place until the stream ends, also when the caller reads it slowly.
/// </summary>
internal sealed partial class ParallelLimitedLlmClient(ILlmClient inner, ILogger<ParallelLimitedLlmClient> logger) : ILlmClient
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<Result<Completion>> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = await AcquireAsync(request.Model, ct);
        return await inner.CompleteAsync(request, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Result<CompletionDelta>> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The body runs at the first read, and the place is given back when the enumerator is disposed.
        // An "await foreach" always disposes it: at the end, after an error, after a cancel and after an early return.
        using var lease = await AcquireAsync(request.Model, ct);
        await foreach (var item in inner.StreamAsync(request, ct))
        {
            yield return item;
        }
    }

    private async Task<Lease> AcquireAsync(ModelProfile model, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(model.Model, _ => new SemaphoreSlim(model.Parallel, model.Parallel));
        if (gate.Wait(0, CancellationToken.None))
        {
            return new Lease(gate);
        }

        var started = Stopwatch.GetTimestamp();
        await gate.WaitAsync(ct);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var waitMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            CallWaited(logger, model.Model, waitMs, model.Parallel);
        }

        return new Lease(gate);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A call to the model {Model} waited {WaitMs} ms, because {Parallel} calls already ran.")]
    private static partial void CallWaited(ILogger logger, string model, long waitMs, int parallel);

    /// <summary>A place in the limit. It gives the place back once.</summary>
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
