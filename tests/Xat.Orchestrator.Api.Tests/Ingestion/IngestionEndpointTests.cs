using System.Net;
using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xat.Orchestrator.Api.Tests.Support;

namespace Xat.Orchestrator.Api.Tests.Ingestion;

/// <summary>Tests the ingestion proxy against a fake xat-rag.</summary>
public sealed class IngestionEndpointTests : IDisposable
{
    private readonly WireMockServer _rag = WireMockServer.Start();

    /// <inheritdoc/>
    public void Dispose() => _rag.Dispose();

    private Dictionary<string, string?> Settings(params string[] more)
    {
        var settings = new Dictionary<string, string?> { ["Upstreams:XatRag:BaseUrl"] = _rag.Url };
        for (var i = 0; i < more.Length; i += 2)
        {
            settings[more[i]] = more[i + 1];
        }

        return settings;
    }

    private void Respond(string method, string path, int status, string body) =>
        _rag.Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    /// <summary>The proxy sends a read request on and gives the status and the body of xat-rag back as they are.</summary>
    [Fact]
    public async Task ListRags_IsForwarded()
    {
        Respond("GET", "/v1/rags", 200, """{"rags":[{"name":"docs","needs_rebuild":false,"documents":3,"chunks":21}],"contexts":["ask-docs"]}""");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/rags", TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"contexts\":[\"ask-docs\"]", text, StringComparison.Ordinal);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>An upload goes on as a multipart body with the same boundary and the same bytes, and the answer 202 with the job ID comes back.</summary>
    [Fact]
    public async Task Upload_IsForwarded_WithTheSameBody()
    {
        Respond("PUT", "/v1/rags/docs/documents", 202, """{"job_id":"j_0123456789abcdef0123"}""");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent("test-boundary-123")
        {
            { new StringContent("""[{"doc_id":"docs:a.md","kind":"markdown","part":"f1"}]""", Encoding.UTF8, "application/json"), "documents" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("# Orders\nThe orders service uses port 8080.")), "f1", "a.md" },
        };

        using var response = await client.PutAsync("/v1/rags/docs/documents", form, TestContext.Current.CancellationToken);
        var answer = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("j_0123456789abcdef0123", answer.GetProperty("job_id").GetString());
        var sent = Assert.Single(_rag.LogEntries).RequestMessage!;
        Assert.Equal("PUT", sent.Method);
        Assert.Contains("test-boundary-123", sent.Headers!["Content-Type"].ToString(), StringComparison.Ordinal);
        var body = Encoding.UTF8.GetString(sent.BodyAsBytes!);
        Assert.Contains("The orders service uses port 8080.", body, StringComparison.Ordinal);
        Assert.Contains("docs:a.md", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A large upload goes through the proxy in one piece. The in-process test server has no body-size limit, so this test shows
    /// that the proxy handles a large body. The real limits of Kestrel are checked in a run of the real service.
    /// </summary>
    [Fact]
    public async Task LargeUpload_IsForwarded()
    {
        Respond("PUT", "/v1/rags/docs/documents", 202, """{"job_id":"j_1"}""");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        var big = new byte[10 * 1024 * 1024];
        Array.Fill(big, (byte)'x');
        using var form = new MultipartFormDataContent { { new ByteArrayContent(big), "f1", "big.txt" } };

        using var response = await client.PutAsync("/v1/rags/docs/documents", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(Assert.Single(_rag.LogEntries).RequestMessage!.BodyAsBytes!.Length > big.Length);
    }

    /// <summary>An upload that declares a size above the limit gets 413 in the normal error form, and xat-rag gets no request at all.</summary>
    [Fact]
    public async Task UploadAboveTheLimit_Gets413_BeforeXatRagIsContacted()
    {
        Respond("PUT", "/v1/rags/docs/documents", 202, """{"job_id":"j_1"}""");
        await using var factory = ApiHost.Create(settings: Settings("Ingestion:MaxUploadBytes", "4096"));
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[64 * 1024]), "f1", "big.bin" } };

        using var response = await client.PutAsync("/v1/rags/docs/documents", form, TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request.too_large", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_rag.LogEntries);
    }

    /// <summary>The other ingestion routes are forwarded with their method and path.</summary>
    [Theory]
    [InlineData("GET", "/v1/rags/docs", "/v1/rags/docs")]
    [InlineData("POST", "/v1/rags/docs/rebuild", "/v1/rags/docs/rebuild")]
    [InlineData("GET", "/v1/jobs/j_0123456789abcdef0123", "/v1/jobs/j_0123456789abcdef0123")]
    [InlineData("DELETE", "/v1/jobs/j_0123456789abcdef0123", "/v1/jobs/j_0123456789abcdef0123")]
    public async Task OtherRoutes_AreForwarded(string method, string path, string upstreamPath)
    {
        Respond(method, upstreamPath, 200, """{"ok":true}""");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_rag.LogEntries).RequestMessage!;
        Assert.Equal((method, upstreamPath), (sent.Method, sent.Path));
    }

    /// <summary>A normal document ID is forwarded. The test of the exact encoding is in IngestionDocumentIdTests.</summary>
    [Fact]
    public async Task DeleteDocument_KeepsTheDocumentId()
    {
        _rag.Given(Request.Create().WithPath("/v1/rags/docs/documents/*").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"deleted":true}"""));
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/v1/rags/docs/documents/docs%3Aorders%20v2.md", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_rag.LogEntries).RequestMessage!;
        Assert.Equal("DELETE", sent.Method);
        Assert.Contains("orders", Uri.UnescapeDataString(sent.AbsolutePath), StringComparison.Ordinal);
        Assert.Contains("v2.md", Uri.UnescapeDataString(sent.AbsolutePath), StringComparison.Ordinal);
    }

    /// <summary>An error of xat-rag, with its status and body, goes to the caller as it is.</summary>
    [Fact]
    public async Task UpstreamError_IsPassedThrough()
    {
        Respond("POST", "/v1/rags/docs/rebuild", 409, """{"error":{"message":"The index does not match.","type":"index_mismatch_error"}}""");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/v1/rags/docs/rebuild", null, TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("index_mismatch_error", body.GetProperty("error").GetProperty("type").GetString());
    }

    /// <summary>
    /// The proxy lets only a short list of headers go on to xat-rag. Credentials, cookies, forwarding headers and headers that change
    /// the meaning of a request never reach xat-rag. The Host header is the loopback address of xat-rag.
    /// </summary>
    [Fact]
    public async Task OnlyAllowedHeaders_GoOn()
    {
        Respond("GET", "/v1/rags", 200, "{}");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/rags");
        request.Headers.Add("Authorization", "Bearer secret-token");
        request.Headers.Add("Cookie", "session=secret-cookie");
        request.Headers.Add("Referer", "http://evil.example/page");
        request.Headers.Add("X-Forwarded-For", "10.1.2.3");
        request.Headers.Add("X-HTTP-Method-Override", "DELETE");
        request.Headers.Add("X-Custom", "not-forwarded");
        request.Headers.Add("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
        request.Headers.Add("tracestate", "vendor=caller-value");
        request.Headers.Add("baggage", "user=secret");
        request.Headers.Add("Accept", "application/json");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var headers = Assert.Single(_rag.LogEntries).RequestMessage!.Headers!;
        Assert.DoesNotContain(headers.Keys, key => key is "Authorization" or "Cookie" or "Referer" or "X-Forwarded-For" or "X-HTTP-Method-Override" or "X-Custom");
        Assert.DoesNotContain(headers.Keys, key => key.Equals("traceparent", StringComparison.OrdinalIgnoreCase) || key.Equals("tracestate", StringComparison.OrdinalIgnoreCase) || key.Equals("baggage", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("application/json", headers["Accept"].ToString());
        Assert.Equal(new Uri(_rag.Url!).Authority, headers["Host"].ToString());
    }

    /// <summary>A path with a trailing slash is refused, because xat-rag could answer it with a redirect to its own address. Nothing goes to xat-rag.</summary>
    [Theory]
    [InlineData("GET", "/v1/rags/")]
    [InlineData("PUT", "/v1/rags/docs/documents/")]
    [InlineData("DELETE", "/v1/rags/docs/documents/")]
    [InlineData("POST", "/v1/rags/docs/rebuild/")]
    public async Task TrailingSlash_IsRefused(string method, string path)
    {
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ingestion.route_not_found", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_rag.LogEntries);
    }

    /// <summary>When xat-rag is not running, the caller gets the normal error form with 503, the unreachable code and Retry-After.</summary>
    [Fact]
    public async Task UpstreamDown_GivesAnUnreachableError()
    {
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        _rag.Stop();

        using var response = await client.GetAsync("/v1/rags", TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal("xat-rag.unreachable", body.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>An operator can turn one route off. That route gives 404 with a clear code and the other routes still work.</summary>
    [Fact]
    public async Task DisabledRoute_Gives404_OthersWork()
    {
        Respond("GET", "/v1/rags", 200, "{}");
        Respond("POST", "/v1/rags/docs/rebuild", 200, "{}");
        await using var factory = ApiHost.Create(settings: Settings("Ingestion:DisabledRoutes:0", "rebuild-rag", "Ingestion:DisabledRoutes:1", "delete-document"));
        using var client = factory.CreateClient();

        using var rebuild = await client.PostAsync("/v1/rags/docs/rebuild", null, TestContext.Current.CancellationToken);
        using var delete = await client.DeleteAsync("/v1/rags/docs/documents/x", TestContext.Current.CancellationToken);
        using var list = await client.GetAsync("/v1/rags", TestContext.Current.CancellationToken);
        var body = await ApiHost.ReadJsonAsync(rebuild);

        Assert.Equal(HttpStatusCode.NotFound, rebuild.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal("ingestion.route_disabled", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(_rag.LogEntries);
    }

    /// <summary>The setting Enabled=false turns the whole proxy off.</summary>
    [Fact]
    public async Task ProxyOff_AllRoutesGive404()
    {
        await using var factory = ApiHost.Create(settings: Settings("Ingestion:Enabled", "false"));
        using var client = factory.CreateClient();

        using var list = await client.GetAsync("/v1/rags", TestContext.Current.CancellationToken);
        using var job = await client.GetAsync("/v1/jobs/j_1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, job.StatusCode);
        Assert.Empty(_rag.LogEntries);
    }

    /// <summary>The proxy forwards only the listed routes. Another path of xat-rag, such as the debug search, or a wrong RAG name never reaches xat-rag.</summary>
    [Theory]
    [InlineData("POST", "/v1/rags/docs/search")]
    [InlineData("POST", "/v1/context")]
    [InlineData("POST", "/v1/query/build")]
    [InlineData("GET", "/v1/rags/DOCS")]
    [InlineData("GET", "/v1/rags/..%2f..%2fhealth")]
    [InlineData("GET", "/health/ready-not")]
    [InlineData("GET", "/v1/jobs/bad%20id")]
    public async Task OtherPaths_AreNotForwarded(string method, string path)
    {
        Respond(method, path, 200, "{}");
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_rag.LogEntries);
    }

    /// <summary>A method that a route does not have is refused by the orchestrator.</summary>
    [Fact]
    public async Task WrongMethod_IsRefused()
    {
        await using var factory = ApiHost.Create(settings: Settings());
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/v1/rags", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(_rag.LogEntries);
    }

    /// <summary>A wrong ingestion setting stops the start. This includes a route name that does not exist, which is usually a spelling mistake.</summary>
    [Theory]
    [InlineData("Ingestion:DisabledRoutes:0", "rebuild-rags")]
    [InlineData("Ingestion:MaxUploadBytes", "10")]
    [InlineData("Ingestion:ActivityTimeoutSeconds", "0")]
    public async Task BadIngestionSettings_StopTheStart(string key, string value)
    {
        await using var factory = ApiHost.Create(settings: Settings(key, value));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
