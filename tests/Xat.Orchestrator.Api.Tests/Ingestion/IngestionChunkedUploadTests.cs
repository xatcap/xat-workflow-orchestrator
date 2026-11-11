using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xat.Orchestrator.Api.Tests.Support;

namespace Xat.Orchestrator.Api.Tests.Ingestion;

/// <summary>Tests an upload without a declared size. The server limits such an upload while it reads the body. This needs a real Kestrel server.</summary>
public sealed class IngestionChunkedUploadTests : IDisposable
{
    private readonly WireMockServer _rag = WireMockServer.Start();

    /// <inheritdoc/>
    public void Dispose() => _rag.Dispose();

    /// <summary>A chunked upload above the limit gets the normal JSON error 413, not an empty answer from the server.</summary>
    [Fact]
    public async Task ChunkedUploadAboveTheLimit_Gets413WithTheErrorBody()
    {
        _rag.Given(Request.Create().WithPath("/v1/rags/docs/documents").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(202).WithBody("{\"job_id\":\"j_1\"}"));
        await using var factory = ApiHost.Create(
            settings: new Dictionary<string, string?>
            {
                ["Upstreams:XatRag:BaseUrl"] = _rag.Url,
                ["Ingestion:MaxUploadBytes"] = "4096",
            },
            kestrel: true);
        using var client = factory.CreateClient();

        // A stream content has no known length, so the client sends it with chunked transfer encoding.
        using var content = new StreamContent(new MemoryStream(new byte[256 * 1024]));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/v1/rags/docs/documents") { Content = content };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request.too_large", body.GetProperty("error").GetProperty("code").GetString());
    }
}
