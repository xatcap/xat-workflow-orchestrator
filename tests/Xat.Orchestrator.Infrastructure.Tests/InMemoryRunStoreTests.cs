using Microsoft.Extensions.Time.Testing;
using Xat.Orchestrator.Application.Jobs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;
using Xat.Orchestrator.Infrastructure.Jobs;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests the in-memory store of asynchronous runs and its retention rules.</summary>
public class InMemoryRunStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

    private InMemoryRunStore Store(int retainFinished = 3, int retentionMinutes = 60) =>
        new(Microsoft.Extensions.Options.Options.Create(new JobsOptions { RetainFinished = retainFinished, RetentionMinutes = retentionMinutes }), _time);

    private RunRecord Run(string id, RunState state = RunState.Queued) =>
        new(id, "p", 1, state, _time.GetUtcNow(), null, state is RunState.Succeeded or RunState.Failed or RunState.Cancelled ? _time.GetUtcNow() : null, null, null, null, WebhookDelivery.None, false);

    /// <summary>A run can be added, read, changed and removed.</summary>
    [Fact]
    public void AddGetUpdateRemove_Work()
    {
        var store = Store();
        store.Add(Run("a"));

        Assert.True(store.TryGet("a", out var found));
        Assert.Equal(RunState.Queued, found.State);
        var updated = store.Update("a", run => run with { State = RunState.Running });
        Assert.Equal(RunState.Running, updated!.State);
        store.Remove("a");
        Assert.False(store.TryGet("a", out _));
        Assert.Null(store.Update("a", run => run));
    }

    /// <summary>When there are more finished runs than the limit, the store removes the oldest finished runs.</summary>
    [Fact]
    public void TooManyFinishedRuns_RemovesTheOldest()
    {
        var store = Store(retainFinished: 2);
        foreach (var id in new[] { "a", "b", "c" })
        {
            store.Add(Run(id));
            _time.Advance(TimeSpan.FromSeconds(1));
            store.Update(id, run => run with { State = RunState.Succeeded, CompletedAt = _time.GetUtcNow() });
        }

        Assert.False(store.TryGet("a", out _));
        Assert.True(store.TryGet("b", out _));
        Assert.True(store.TryGet("c", out _));
    }

    /// <summary>A finished run is removed after the retention time. The removal happens at the next change of a run.</summary>
    [Fact]
    public void OldFinishedRun_IsRemovedAfterTheRetentionTime()
    {
        var store = Store(retentionMinutes: 10);
        store.Add(Run("old", RunState.Succeeded));
        _time.Advance(TimeSpan.FromMinutes(11));

        store.Add(Run("new"));

        Assert.False(store.TryGet("old", out _));
        Assert.True(store.TryGet("new", out _));
    }

    /// <summary>A caller that reads a finished run after its retention time gets nothing, also when no write came in between.</summary>
    [Fact]
    public void ExpiredRun_IsNotServed_AfterTheRetentionTime()
    {
        var store = Store(retentionMinutes: 10);
        store.Add(Run("done", RunState.Succeeded));
        Assert.True(store.TryGet("done", out _));

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.False(store.TryGet("done", out _));
    }

    /// <summary>The trim works at the head of the queue, so many finished runs do not make a write slow. This test finishes 20000 runs and checks the count.</summary>
    [Fact]
    public void ManyFinishedRuns_StayInsideTheLimit()
    {
        var store = Store(retainFinished: 100);

        for (var i = 0; i < 20_000; i++)
        {
            store.Add(Run($"r{i}", RunState.Succeeded));
            _time.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.False(store.TryGet("r0", out _));
        Assert.False(store.TryGet("r19899", out _));
        Assert.True(store.TryGet("r19900", out _));
        Assert.True(store.TryGet("r19999", out _));
    }

    /// <summary>A run that waits or runs is never removed, also when it is old and the limit is small.</summary>
    [Fact]
    public void ActiveRuns_AreNeverRemoved()
    {
        var store = Store(retainFinished: 1, retentionMinutes: 1);
        store.Add(Run("queued"));
        store.Add(Run("running", RunState.Running));
        _time.Advance(TimeSpan.FromHours(5));

        store.Add(Run("finished", RunState.Succeeded));

        Assert.True(store.TryGet("queued", out _));
        Assert.True(store.TryGet("running", out _));
    }

    /// <summary>Many threads can change runs at the same time without lost updates. Each update sees the result of the one before.</summary>
    [Fact]
    public async Task ParallelUpdates_AreAtomic()
    {
        var store = Store();
        store.Add(Run("a") with { Error = null });
        var counter = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(
            () => store.Update("a", run =>
            {
                counter++;
                return run with { Version = run.Version + 1 };
            }),
            TestContext.Current.CancellationToken)));

        Assert.Equal(200, counter);
        store.TryGet("a", out var final);
        Assert.Equal(201, final!.Version);
    }

    /// <summary>The error of a run can be stored and read.</summary>
    [Fact]
    public void Error_IsStored()
    {
        var store = Store();
        store.Add(Run("a"));

        store.Update("a", run => run with { State = RunState.Failed, Error = new PipelineError("x.y", "no", ErrorKind.Timeout) });

        store.TryGet("a", out var found);
        Assert.Equal("x.y", found!.Error!.Code);
    }
}
