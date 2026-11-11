using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Tests.Support;

namespace Xat.Orchestrator.Infrastructure.Tests.XatRag;

/// <summary>Tests the retry and the time limit of the upstream clients.</summary>
public sealed class UpstreamResilienceTests : IDisposable
{
    private readonly Harness _harness = new(new Dictionary<string, string?> { ["Upstreams:XatRag:RetryCount"] = "2" });

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    private static RetrievalQuery Query => new("ask-docs", "Q", 5);

    /// <summary>A 503 is tried again, and the second try gives the answer.</summary>
    [Fact]
    public async Task Retries_AfterA503_AndSucceeds()
    {
        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost()).InScenario("retry").WillSetStateTo("second")
            .RespondWith(Response.Create().WithStatusCode(503).WithHeader("Retry-After", "0").WithBody("""{"error":{"message":"busy","type":"overloaded_error"}}"""));
        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost()).InScenario("retry").WhenStateIs("second")
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"chunks":[],"omitted":[]}"""));

        var result = await _harness.Get<IRetrievalClient>().RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, $"{result.Error?.Code} {result.Error?.Message} requests={_harness.Rag.LogEntries.Count}");
        Assert.Equal(2, _harness.Rag.LogEntries.Count);
    }

    /// <summary>A 400 is never tried again, because the same request gives the same error.</summary>
    [Fact]
    public async Task DoesNotRetry_A400()
    {
        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("""{"error":{"message":"bad","type":"invalid_request_error"}}"""));

        var result = await _harness.Get<IRetrievalClient>().RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Single(_harness.Rag.LogEntries);
    }

    /// <summary>After the last try, the error of the upstream goes to the caller.</summary>
    [Fact]
    public async Task GivesTheError_WhenAllTriesFail()
    {
        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503).WithHeader("Retry-After", "0").WithBody("""{"error":{"message":"busy","type":"overloaded_error"}}"""));

        var result = await _harness.Get<IRetrievalClient>().RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorKind.Overloaded, result.Error!.Kind);
        Assert.Equal(3, _harness.Rag.LogEntries.Count);
    }

    /// <summary>A slow upstream ends with a timeout error when it passes the time limit.</summary>
    [Fact]
    public async Task SlowUpstream_GivesATimeoutError()
    {
        using var harness = new Harness(new Dictionary<string, string?> { ["Upstreams:XatRag:RequestTimeoutSeconds"] = "1" });
        harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(4)).WithBody("""{"chunks":[]}"""));

        var result = await harness.Get<IRetrievalClient>().RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorKind.Timeout, result.Error!.Kind);
    }

    /// <summary>When the caller cancels, the call stops and the cancel goes to the caller. It is not an upstream error.</summary>
    [Fact]
    public async Task CallerCancel_IsNotAnUpstreamError()
    {
        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(5)).WithBody("""{"chunks":[]}"""));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _harness.Get<IRetrievalClient>().RetrieveAsync(Query, cts.Token));
    }
}
