using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests the xat-rag adapter against a fake xat-rag.</summary>
public sealed class XatRagRetrievalClientTests : IDisposable
{
    private const string ContextBody = """
        {"chunks":[
           {"chunk_id":"docs:orders.md#h/ports","parent_id":null,"kind":"section","score":1.34,"size_tokens":40,
            "header":"Orders > Ports","text":"The orders service uses port 8080.",
            "source":{"rag":"docs","doc_id":"docs:orders.md","source_kind":"markdown","uri":"orders.md","title":"Orders","version":"3","span":{"type":"line","line_start":1,"line_end":4}},
            "metadata":{"team":"core"}}],
         "omitted":[{"chunk_id":"docs:orders.md#h/other","reason":"limit"}],"debug":null}
        """;

    private readonly Harness _harness;

    /// <summary>Starts the fake services.</summary>
    public XatRagRetrievalClientTests()
    {
        _harness = new Harness();
    }

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    private IRetrievalClient Client => _harness.Get<IRetrievalClient>();

    private static RetrievalQuery Query => new("ask-docs", "Which port?", 5);

    private void Respond(int status, string body, string? retryAfter = null)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        if (retryAfter is not null)
        {
            response = response.WithHeader("Retry-After", retryAfter);
        }

        _harness.Rag.Given(Request.Create().WithPath("/v1/context").UsingPost()).RespondWith(response);
    }

    /// <summary>A good answer maps to domain chunks, and the text is in an untrusted wrapper.</summary>
    [Fact]
    public async Task Retrieve_MapsTheChunks()
    {
        Respond(200, ContextBody);

        var result = await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var chunk = Assert.Single(result.Value!.Chunks);
        Assert.Equal("docs:orders.md#h/ports", chunk.ChunkId);
        Assert.Equal("The orders service uses port 8080.", chunk.Text.Value);
        Assert.Equal("Orders > Ports", chunk.Header.Value);
        Assert.Equal(("docs", "markdown", "Orders", "3"), (chunk.Source.Rag, chunk.Source.SourceKind, chunk.Source.Title, chunk.Source.Version));
        Assert.Equal(1, result.Value.OmittedCount);
        Assert.Equal("[untrusted text]", chunk.Text.ToString());
    }

    /// <summary>The request has the context, the question and the number of chunks in the form that xat-rag expects.</summary>
    [Fact]
    public async Task Retrieve_SendsTheRequestBody()
    {
        Respond(200, ContextBody);

        await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        var entry = Assert.Single(_harness.Rag.LogEntries);
        Assert.Equal("""{"context":"ask-docs","question":"Which port?","chunks":5}""", entry.RequestMessage!.Body);
    }

    /// <summary>An empty chunk list is a success, not an error.</summary>
    [Fact]
    public async Task Retrieve_EmptyChunks_IsASuccess()
    {
        Respond(200, """{"chunks":[],"omitted":[],"debug":null}""");

        var result = await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Chunks);
    }

    /// <summary>Each error of xat-rag maps to one kind. The retry time is kept.</summary>
    [Theory]
    [InlineData(400, "invalid_request_error", ErrorKind.Validation)]
    [InlineData(404, "not_found_error", ErrorKind.NotFound)]
    [InlineData(409, "index_mismatch_error", ErrorKind.Conflict)]
    [InlineData(503, "overloaded_error", ErrorKind.Overloaded)]
    [InlineData(503, "service_unavailable_error", ErrorKind.UpstreamUnavailable)]
    [InlineData(504, "stage_timeout_error", ErrorKind.Timeout)]
    [InlineData(500, "internal_error", ErrorKind.UpstreamFailed)]
    public async Task Retrieve_MapsErrors(int status, string type, ErrorKind expected)
    {
        Respond(status, $$$"""{"error":{"message":"A problem.","type":"{{{type}}}"}}""", retryAfter: status == 503 ? "7" : null);

        var result = await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error!.Kind);
        Assert.Equal($"xat-rag.{type}", result.Error.Code);
        Assert.Equal("xat-rag", result.Error.Source);
        Assert.Equal(status == 503 ? TimeSpan.FromSeconds(7) : null, result.Error.RetryAfter);
    }

    /// <summary>A body that is not the error format, for example HTML, still gives an error from the status code.</summary>
    [Fact]
    public async Task Retrieve_NonJsonErrorBody_StillMapsTheStatus()
    {
        Respond(400, "<html>Bad host</html>");

        var result = await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Equal("xat-rag.http_400", result.Error.Code);
    }

    /// <summary>A service that is not running gives an unavailable error, not an exception.</summary>
    [Fact]
    public async Task Retrieve_WhenTheServiceIsDown_GivesUnavailable()
    {
        _harness.Rag.Stop();

        var result = await Client.RetrieveAsync(Query, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorKind.UpstreamUnavailable, result.Error!.Kind);
        Assert.Equal("xat-rag.unreachable", result.Error.Code);
    }
}
