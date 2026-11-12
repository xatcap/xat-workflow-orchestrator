using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Xat.Orchestrator.Application.Jobs;

/// <summary>
/// Holds the runs that wait for a worker, and the cancel tokens of the runs that execute. One lock covers both, so a run is
/// always either waiting or running, and a cancel always finds it. A cancelled waiting run leaves the queue at once,
/// so it frees its place and its input.
/// </summary>
/// <param name="limit">The highest number of waiting runs.</param>
internal sealed class RunQueue(int limit)
{
    private readonly Lock _lock = new();
    private readonly Channel<string> _order = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Dictionary<string, QueuedRun> _waiting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);

    /// <summary>The result of a cancel.</summary>
    public enum CancelOutcome
    {
        /// <summary>The run was not in the queue and did not run.</summary>
        NotFound,

        /// <summary>The run waited. It left the queue.</summary>
        RemovedFromQueue,

        /// <summary>The run executes. Its token is cancelled, and the run stops soon.</summary>
        Signalled,
    }

    /// <summary>Adds a run at the end of the queue.</summary>
    /// <param name="item">The run.</param>
    /// <returns><see langword="false"/> when the queue is full.</returns>
    public bool TryEnqueue(QueuedRun item)
    {
        lock (_lock)
        {
            if (_waiting.Count >= limit)
            {
                return false;
            }

            _waiting[item.RunId] = item;
            _order.Writer.TryWrite(item.RunId);
            return true;
        }
    }

    /// <summary>
    /// Takes the waiting runs in order, until the token stops the loop. Each run gets its cancel token in the same step,
    /// so no cancel is lost. The worker must call <see cref="Release"/> when the run ends.
    /// </summary>
    /// <param name="stoppingToken">The token that stops the worker. The token of each run is linked to it.</param>
    /// <returns>The runs, with their tokens.</returns>
    public async IAsyncEnumerable<(QueuedRun Item, CancellationTokenSource Cancel)> ClaimAllAsync(
        [EnumeratorCancellation] CancellationToken stoppingToken)
    {
        await foreach (var runId in _order.Reader.ReadAllAsync(stoppingToken))
        {
            if (TryClaim(runId, stoppingToken, out var claimed))
            {
                yield return claimed;
            }
        }
    }

    /// <summary>Forgets the token of a run that ended, and frees it.</summary>
    /// <param name="runId">The ID of the run.</param>
    public void Release(string runId)
    {
        CancellationTokenSource? cancel;
        lock (_lock)
        {
            _running.Remove(runId, out cancel);
        }

        cancel?.Dispose();
    }

    /// <summary>Cancels a run that waits or that executes.</summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="removed">The run, when it left the queue.</param>
    /// <returns>What the cancel did.</returns>
    public CancelOutcome Cancel(string runId, out QueuedRun? removed)
    {
        CancellationTokenSource? cancel;
        lock (_lock)
        {
            if (_waiting.Remove(runId, out var item))
            {
                removed = item;
                return CancelOutcome.RemovedFromQueue;
            }

            removed = null;
            if (!_running.TryGetValue(runId, out cancel))
            {
                return CancelOutcome.NotFound;
            }
        }

        // The cancel runs the callbacks of the run, so it runs outside the lock.
        try
        {
            cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run ended at the same moment. It is finished, so there is nothing to cancel.
        }

        return CancelOutcome.Signalled;
    }

    private bool TryClaim(string runId, CancellationToken stoppingToken, out (QueuedRun Item, CancellationTokenSource Cancel) claimed)
    {
        lock (_lock)
        {
            if (!_waiting.Remove(runId, out var item))
            {
                // A cancel removed the run while it waited.
                claimed = default;
                return false;
            }

            var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _running[runId] = cancel;
            claimed = (item, cancel);
            return true;
        }
    }
}
