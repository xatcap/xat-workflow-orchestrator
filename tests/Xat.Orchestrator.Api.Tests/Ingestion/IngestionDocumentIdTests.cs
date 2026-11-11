using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xat.Orchestrator.Api.Tests.Support;

namespace Xat.Orchestrator.Api.Tests.Ingestion;

/// <summary>Tests that the delete-document route keeps the document ID exact, and refuses an ID that could point to another path.</summary>
public sealed class IngestionDocumentIdTests : IDisposable
{
    private readonly WireMockServer _rag = WireMockServer.Start();

    /// <inheritdoc/>
    public void Dispose() => _rag.Dispose();

    private Dictionary<string, string?> Settings() => new() { ["Upstreams:XatRag:BaseUrl"] = _rag.Url };

    /// <summary>
    /// A document ID can hold a slash. The caller sends the slash as %2F. The proxy keeps the original text,
    /// so xat-rag gets the same ID. An encoded percent sign stays an encoded percent sign.
    /// The fake xat-rag shows the address with one level of decoding, so the expected text has one level less than the text that the caller sent.
    /// An old fault encoded the text a second time. That fault shows as <c>%252F</c> where the test expects <c>%2F</c>.
    /// </summary>
    [Theory]
    [InlineData("docs%3Asub%2Forders.md", "sub%2Forders.md", "sub%252Forders.md")]
    [InlineData("a%252Fb", "a%2Fb", "a%252Fb")]
    [InlineData("name%20with%20space.md", "name with space.md", "name%2520with")]
    public async Task DeleteDocument_ForwardsTheDocumentIdExactly(string id, string mustContain, string mustNotContain)
    {
        _rag.Given(Request.Create().WithPath("/v1/rags/docs/documents/*").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"deleted\":true}"));
        await using var factory = ApiHost.Create(settings: Settings(), kestrel: true);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/v1/rags/docs/documents/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_rag.LogEntries).RequestMessage!;
        Assert.Contains(mustContain, sent.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(mustNotContain, sent.Url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document ID with a dot segment, also an encoded one, is refused, because it could point to another path. Nothing goes to xat-rag.
    /// The request goes over a plain socket, because an HTTP client library removes dot segments before it sends the request.
    /// </summary>
    [Theory]
    [InlineData("x/../y")]
    [InlineData("..")]
    [InlineData("x%2F..%2Fy")]
    [InlineData("%2e%2e")]
    [InlineData("a%5C..%5Cb")]
    [InlineData("%252e%252e")]
    public async Task DeleteDocument_WithADotSegment_IsRefused(string id)
    {
        await using var factory = ApiHost.Create(settings: Settings(), kestrel: true);
        using var client = factory.CreateClient();

        var status = await RawHttp.SendAsync(client.BaseAddress!, "DELETE", $"/v1/rags/docs/documents/{id}");

        // Kestrel removes a plain dot segment from the path before routing, so the path can become a path that has no delete route (405).
        // The proxy refuses the other forms itself (404). In both cases nothing goes to xat-rag.
        Assert.True(status is 404 or 405, $"Status {status}.");
        Assert.Empty(_rag.LogEntries);
    }
}
