using System.Diagnostics.CodeAnalysis;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>A simple in-memory store with the same atomic update rule as the real store. It keeps every run.</summary>
internal sealed class FakeRunStore : IRunStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RunRecord> _runs = [];

    public IReadOnlyList<RunRecord> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _runs.Values];
            }
        }
    }

    public void Add(RunRecord record)
    {
        lock (_gate)
        {
            _runs[record.RunId] = record;
        }
    }

    public bool TryGet(string runId, [MaybeNullWhen(false)] out RunRecord record)
    {
        lock (_gate)
        {
            return _runs.TryGetValue(runId, out record);
        }
    }

    public RunRecord? Update(string runId, Func<RunRecord, RunRecord> change)
    {
        lock (_gate)
        {
            return _runs.TryGetValue(runId, out var current) ? _runs[runId] = change(current) : null;
        }
    }

    public void Remove(string runId)
    {
        lock (_gate)
        {
            _runs.Remove(runId);
        }
    }
}
