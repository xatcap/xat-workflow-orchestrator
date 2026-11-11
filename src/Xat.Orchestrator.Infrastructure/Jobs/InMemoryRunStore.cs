using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Jobs;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Infrastructure.Jobs;

/// <summary>
/// Keeps the asynchronous runs in memory. A restart loses them. The store removes finished runs that are older than the retention time,
/// and the oldest finished runs when there are more than the limit. Runs that wait or run are never removed.
/// A finished run is also removed when a caller reads it after its retention time, so a result never outlives its retention time.
/// </summary>
internal sealed class InMemoryRunStore(IOptions<JobsOptions> options, TimeProvider clock) : IRunStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RunRecord> _runs = new(StringComparer.Ordinal);

    // The IDs of the finished runs, in the order in which they finished. A trim looks only at the head of this queue.
    private readonly Queue<string> _finishedOrder = new();
    private readonly HashSet<string> _finished = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Add(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _runs[record.RunId] = record;
            if (record.IsFinished && _finished.Add(record.RunId))
            {
                _finishedOrder.Enqueue(record.RunId);
            }

            Trim();
        }
    }

    /// <inheritdoc/>
    public bool TryGet(string runId, [MaybeNullWhen(false)] out RunRecord record)
    {
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out record) && record.IsFinished && IsExpired(record))
            {
                Drop(runId);
                record = null;
            }

            return record is not null;
        }
    }

    /// <inheritdoc/>
    public RunRecord? Update(string runId, Func<RunRecord, RunRecord> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var current))
            {
                return null;
            }

            var updated = change(current);
            _runs[runId] = updated;
            if (updated.IsFinished && _finished.Add(runId))
            {
                _finishedOrder.Enqueue(runId);
                Trim();
            }

            return updated;
        }
    }

    /// <inheritdoc/>
    public void Remove(string runId)
    {
        lock (_gate)
        {
            Drop(runId);
        }
    }

    /// <summary>Removes a run. The caller must hold the lock. The queue can keep the ID until a trim passes it, and the trim ignores it then.</summary>
    private void Drop(string runId)
    {
        _runs.Remove(runId);
        _finished.Remove(runId);
    }

    private bool IsExpired(RunRecord record) =>
        clock.GetUtcNow() - (record.CompletedAt ?? record.CreatedAt) > TimeSpan.FromMinutes(options.Value.RetentionMinutes);

    /// <summary>Removes old finished runs from the head of the queue. The caller must hold the lock.</summary>
    private void Trim()
    {
        while (_finishedOrder.TryPeek(out var oldest))
        {
            if (!_finished.Contains(oldest))
            {
                // The run is gone already.
                _finishedOrder.Dequeue();
                continue;
            }

            var tooMany = _finished.Count > options.Value.RetainFinished;
            if (!tooMany && !(_runs.TryGetValue(oldest, out var record) && IsExpired(record)))
            {
                return;
            }

            _finishedOrder.Dequeue();
            Drop(oldest);
        }
    }
}
