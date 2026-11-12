using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Infrastructure.Options.Llm;
using Xat.Orchestrator.Infrastructure.XatLlmEngine;

namespace Xat.Orchestrator.Infrastructure.Tests.XatLlmEngine;

/// <summary>Tests the limit of calls that run at the same time for each model.</summary>
public class ParallelLimitedLlmClientTests
{
    private static LlmRequest Request(string model = "local/a", int parallel = 1, string alias = "default") =>
        new(new ModelProfile(alias, model, parallel, []), [new ChatMessage(ChatRole.User, "Hello")], null, null);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>With a limit of 1, the second call waits until the first call ends.</summary>
    [Fact]
    public async Task SecondCall_WaitsForTheFirst()
    {
        using var inner = new BlockingClient();
        var client = new ParallelLimitedLlmClient(inner, NullLogger<ParallelLimitedLlmClient>.Instance);

        var first = client.CompleteAsync(Request(), Token);
        var second = client.CompleteAsync(Request(), Token);
        await inner.WaitForCallsAsync(1);

        Assert.Equal(1, inner.Running);
        inner.ReleaseOne();
        await first;
        await inner.WaitForCallsAsync(2);
        inner.ReleaseOne();
        await second;
        Assert.Equal(1, inner.MaxRunning);
    }

    /// <summary>The limit is for the model ID. Two aliases of the same model share it. Another model has its own limit.</summary>
    [Fact]
    public async Task Limit_IsForTheModel_NotForTheAlias()
    {
        using var inner = new BlockingClient();
        var client = new ParallelLimitedLlmClient(inner, NullLogger<ParallelLimitedLlmClient>.Instance);

        var a = client.CompleteAsync(Request(alias: "one"), Token);
        var b = client.CompleteAsync(Request(alias: "two"), Token);
        var other = client.CompleteAsync(Request(model: "local/b"), Token);
        await inner.WaitForCallsAsync(2);

        Assert.Equal(2, inner.Running);
        inner.ReleaseAll();
        await Task.WhenAll(a, b, other);
    }

    /// <summary>A failure and an exception give the place back, so the next call can run.</summary>
    [Fact]
    public async Task FailureAndException_GiveThePlaceBack()
    {
        using var inner = new BlockingClient { Throw = true };
        var client = new ParallelLimitedLlmClient(inner, NullLogger<ParallelLimitedLlmClient>.Instance);
        inner.ReleaseAll();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(Request(), Token));
        inner.Throw = false;
        inner.Result = Result<Completion>.Fail(new PipelineError("x.failed", "Failed.", ErrorKind.UpstreamFailed));
        Assert.False((await client.CompleteAsync(Request(), Token)).IsSuccess);
        inner.Result = Result<Completion>.Ok(new Completion("ok", "stop", null));
        Assert.True((await client.CompleteAsync(Request(), Token)).IsSuccess);
    }

    /// <summary>A cancel while the call waits ends the wait and takes no place.</summary>
    [Fact]
    public async Task CancelWhileWaiting_TakesNoPlace()
    {
        using var inner = new BlockingClient();
        var client = new ParallelLimitedLlmClient(inner, NullLogger<ParallelLimitedLlmClient>.Instance);
        var first = client.CompleteAsync(Request(), Token);
        await inner.WaitForCallsAsync(1);
        using var cancel = new CancellationTokenSource();

        var waiting = client.CompleteAsync(Request(), cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        inner.ReleaseAll();
        await first;
        Assert.True((await client.CompleteAsync(Request(), Token)).IsSuccess);
    }

    /// <summary>A stream holds the place until it ends, and gives it back also when the reader stops early.</summary>
    [Fact]
    public async Task Stream_HoldsThePlaceUntilItEnds()
    {
        using var inner = new BlockingClient();
        inner.ReleaseAll();
        var client = new ParallelLimitedLlmClient(inner, NullLogger<ParallelLimitedLlmClient>.Instance);

        await using (var reader = client.StreamAsync(Request(), Token).GetAsyncEnumerator(Token))
        {
            Assert.True(await reader.MoveNextAsync());
            var blocked = client.CompleteAsync(Request(), Token);
            await Task.Delay(100, Token);
            Assert.False(blocked.IsCompleted);

            // The reader stops before the end of the stream. The dispose gives the place back.
            await reader.DisposeAsync();
            Assert.True((await blocked).IsSuccess);
        }
    }

    /// <summary>All aliases of one model must use the same limit.</summary>
    [Fact]
    public void Validator_RefusesDifferentLimitsForOneModel()
    {
        var options = new LlmOptions
        {
            DefaultModel = "one",
            Models =
            {
                ["one"] = new ModelOptions { Model = "local/a", Parallel = 1 },
                ["two"] = new ModelOptions { Model = "local/a", Parallel = 2 },
            },
        };

        var result = new LlmOptionsValidator().Validate(null, options);

        Assert.Contains(result.Failures!, failure => failure.Contains("one, two", StringComparison.Ordinal));
    }

    /// <summary>An LLM client that holds each call until the test releases it.</summary>
    private sealed class BlockingClient : ILlmClient, IDisposable
    {
        private readonly SemaphoreSlim _release = new(0);
        private int _running;
        private int _calls;
        private int _maxRunning;

        public int Running => Volatile.Read(ref _running);

        public int MaxRunning => Volatile.Read(ref _maxRunning);

        public bool Throw { get; set; }

        public Result<Completion> Result { get; set; } = Result<Completion>.Ok(new Completion("ok", "stop", null));

        public void ReleaseOne() => _release.Release();

        public void ReleaseAll() => _release.Release(1000);

        public void Dispose() => _release.Dispose();

        public async Task WaitForCallsAsync(int count)
        {
            while (Volatile.Read(ref _calls) < count)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }

        public async Task<Result<Completion>> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Enter();
            try
            {
                await _release.WaitAsync(ct);
                return Throw ? throw new InvalidOperationException("Test.") : Result;
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public async IAsyncEnumerable<Result<CompletionDelta>> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Enter();
            try
            {
                for (var i = 0; i < 3; i++)
                {
                    await _release.WaitAsync(ct);
                    yield return Result<CompletionDelta>.Ok(new CompletionDelta("x", null, null));
                }
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private void Enter()
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxRunning)) && Interlocked.CompareExchange(ref _maxRunning, now, seen) != seen)
            {
            }
        }
    }
}
