using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Application.Tests.Fakes;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Tests.Engine;

/// <summary>Tests the retry of idempotent steps, the rule for the step traits, the run log and the trace spans.</summary>
public class EngineFeatureTests
{
    private static RunHandle Start(ServiceProvider services, PipelineDefinition pipeline, string inputJson, bool stream = false) =>
        services.GetRequiredService<PipelineRunner>().Start(pipeline, TestData.Json(inputJson), new RunRequest(stream), TestContext.Current.CancellationToken);

    private static FakeRetrievalClient FailingFirst(ErrorKind kind, int failures = 1)
    {
        var calls = 0;
        return new FakeRetrievalClient
        {
            OnRetrieve = _ => Interlocked.Increment(ref calls) <= failures
                ? Result<RetrievedSet>.Fail(new PipelineError("xat-rag.http_502", "xat-rag answered with status 502.", kind))
                : Result<RetrievedSet>.Ok(new RetrievedSet([TestData.Chunk("c1", "Orders", "The orders service uses port 8080.")], 0)),
        };
    }

    /// <summary>An idempotent step that fails with an upstream failure runs again once, and the run then answers.</summary>
    [Fact]
    public async Task IdempotentStep_RunsAgainAfterAnUpstreamFailure()
    {
        var rag = FailingFirst(ErrorKind.UpstreamFailed);
        using var services = TestHost.Build(rag);

        var result = await Start(services, TestData.RagQa(), """{"prompt":"Which port?"}""").Completion;

        Assert.True(result.IsSuccess);
        Assert.Equal(2, rag.Queries.Count);
        Assert.Single(result.Value.Steps, step => step.StepId == "retrieve");
    }

    /// <summary>The engine does not repeat the HTTP retries: an unavailable upstream, a timeout or a validation error is not retried.</summary>
    [Theory]
    [InlineData(ErrorKind.UpstreamUnavailable)]
    [InlineData(ErrorKind.Timeout)]
    [InlineData(ErrorKind.Validation)]
    [InlineData(ErrorKind.RateLimited)]
    public async Task IdempotentStep_IsNotRetried_ForOtherFailures(ErrorKind kind)
    {
        var rag = FailingFirst(kind);
        using var services = TestHost.Build(rag);

        var result = await Start(services, TestData.RagQa(), """{"prompt":"Which port?"}""").Completion;

        Assert.False(result.IsSuccess);
        Assert.Single(rag.Queries);
    }

    /// <summary>The setting sets the number of retries, and 0 turns the retry off. The last error is the error of the run.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task RetryCount_SetsTheNumberOfTries(int retries, int expectedCalls)
    {
        var rag = FailingFirst(ErrorKind.UpstreamFailed, failures: 10);
        using var services = TestHost.Build(rag, configure: s => s.Configure<RunnerOptions>(o => o.IdempotentRetryCount = retries));

        var result = await Start(services, TestData.RagQa(), """{"prompt":"Which port?"}""").Completion;

        Assert.Equal("xat-rag.http_502", result.Error!.Code);
        Assert.Equal(expectedCalls, rag.Queries.Count);
    }

    /// <summary>A step that already sent an event to the caller is not run again, because the caller would see the event twice.</summary>
    [Fact]
    public async Task IdempotentStep_ThatSentAnEvent_IsNotRetried()
    {
        var step = new EmittingFailStep();
        using var services = TestHost.Build(configure: s =>
        {
            s.AddSingleton(step);
            s.AddPipelineStep<EmittingFailStep, EmptyOptions>("test.emit-fail", StepTraits.Idempotent, "Sends a delta, then fails.");
        });
        var pipeline = TestData.Pipeline("t", [TestData.Step("e", "test.emit-fail"), TestData.Step("g", "llm.generate")]);

        var handle = Start(services, pipeline, "{}", stream: true);
        await foreach (var _ in handle.Events!.ReadAllAsync(TestContext.Current.CancellationToken))
        {
        }

        var result = await handle.Completion;
        Assert.Equal("test.failed", result.Error!.Code);
        Assert.Equal(1, step.Calls);
    }

    /// <summary>The start refuses a step type that calls the LLM and is idempotent, because the engine must not repeat an LLM call.</summary>
    [Fact]
    public void Registry_RefusesAnLlmStepThatIsIdempotent()
    {
        using var services = TestHost.Build(configure: s =>
            s.AddPipelineStep<EmittingFailStep, EmptyOptions>("test.bad", StepTraits.CallsLlm | StepTraits.Idempotent, "Not valid."));

        var exception = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IStepRegistry>());
        Assert.Contains("test.bad", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The run log has IDs, names and codes, but never the input of the caller.</summary>
    [Fact]
    public async Task RunLog_HasIdsAndCodes_ButNoInput()
    {
        var sink = new ListLoggerProvider();
        using var services = TestHost.Build(configure: s => s.AddLogging(builder => builder.AddProvider(sink).SetMinimumLevel(LogLevel.Debug)));

        var result = await Start(services, TestData.RagQa(), """{"prompt":"secret-question"}""").Completion;

        Assert.True(result.IsSuccess);
        Assert.Contains(sink.Lines, line => line.Contains(result.Value.RunId, StringComparison.Ordinal) && line.Contains("rag-qa", StringComparison.Ordinal));
        Assert.Contains(sink.Lines, line => line.Contains("step retrieve (rag.retrieve)", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Lines, line => line.Contains("secret-question", StringComparison.Ordinal));
    }

    /// <summary>Each step makes one span with the run ID, and a failed step has the error code.</summary>
    [Fact]
    public async Task Spans_HaveTheRunId_AndTheErrorCode()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Xat.Orchestrator.Pipelines",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var rag = FailingFirst(ErrorKind.UpstreamFailed, failures: 10);
        using var services = TestHost.Build(rag);

        var handle = Start(services, TestData.RagQa(), """{"prompt":"Which port?"}""");
        await handle.Completion;

        var mine = spans.Where(span => (string?)span.GetTagItem("pipeline.run.id") == handle.RunId).ToList();
        var retrieve = Assert.Single(mine, span => (string?)span.GetTagItem("pipeline.step.id") == "retrieve");
        Assert.Equal("xat-rag.http_502", retrieve.GetTagItem("error.code"));
        Assert.Equal(ActivityStatusCode.Error, retrieve.Status);
    }

    /// <summary>The options of the test step.</summary>
    public sealed class EmptyOptions
    {
    }

    /// <summary>A step that sends a delta and then fails with an upstream failure.</summary>
    public sealed class EmittingFailStep : IPipelineStep<EmptyOptions>
    {
        private int _calls;

        /// <summary>Gets the number of calls.</summary>
        public int Calls => _calls;

        /// <inheritdoc/>
        public async ValueTask<StepOutcome> ExecuteAsync(StepContext context, EmptyOptions options, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            await context.Run.Events.EmitAsync(new RunEvent.Delta("part"), ct);
            return StepOutcome.Fail(new PipelineError("test.failed", "The test step failed.", ErrorKind.UpstreamFailed));
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(formatter(state, exception));
        }
    }
}
