using System.Diagnostics.CodeAnalysis;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>
/// The store of asynchronous runs. The store removes old finished runs by its own rules, so a caller must read a result
/// before the store removes it.
/// </summary>
public interface IRunStore
{
    /// <summary>Adds a new run.</summary>
    /// <param name="record">The run.</param>
    void Add(RunRecord record);

    /// <summary>Reads a run.</summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="record">The run, when the store holds it.</param>
    /// <returns><see langword="true"/> when the store holds the run.</returns>
    bool TryGet(string runId, [MaybeNullWhen(false)] out RunRecord record);

    /// <summary>
    /// Changes a run in one atomic step. The function must be fast and must not call the store.
    /// Two changes of the same run never run at the same time.
    /// </summary>
    /// <param name="runId">The ID of the run.</param>
    /// <param name="change">The function that makes the new run from the old run.</param>
    /// <returns>The new run, or <see langword="null"/> when the store does not hold the run.</returns>
    RunRecord? Update(string runId, Func<RunRecord, RunRecord> change);

    /// <summary>Removes a run.</summary>
    /// <param name="runId">The ID of the run.</param>
    void Remove(string runId);
}
