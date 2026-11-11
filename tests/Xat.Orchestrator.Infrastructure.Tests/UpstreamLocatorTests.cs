using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Discovery;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Tests;

/// <summary>Tests how the orchestrator finds xat-rag and xat-llm-engine.</summary>
public sealed class UpstreamLocatorTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("xat-locator-").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static ReadyFileLocator Locator(UpstreamEndpointOptions rag) =>
        new(Microsoft.Extensions.Options.Options.Create(new UpstreamOptions { XatRag = rag }));

    /// <summary>A fixed base URL is used as it is.</summary>
    [Fact]
    public async Task FixedBaseUrl_IsUsed()
    {
        var result = await Locator(new UpstreamEndpointOptions { BaseUrl = "http://127.0.0.1:9999" }).LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);

        Assert.Equal("http://127.0.0.1:9999/", result.Value!.ToString());
    }

    /// <summary>The URL comes from the ready file. After a restart with a new port, an invalidate makes the locator read the file again.</summary>
    [Fact]
    public async Task ReadyFile_IsRead_AndReadAgainAfterInvalidate()
    {
        var file = Path.Combine(_folder, "ready.json");
        await File.WriteAllTextAsync(file, """{"url":"http://127.0.0.1:50001","pid":1,"version":"0.1.0"}""", TestContext.Current.CancellationToken);
        var locator = Locator(new UpstreamEndpointOptions { ReadyFile = file });

        var first = await locator.LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(file, """{"url":"http://127.0.0.1:50002","pid":2,"version":"0.1.0"}""", TestContext.Current.CancellationToken);
        var cached = await locator.LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);
        locator.Invalidate(Upstream.XatRag);
        var second = await locator.LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);

        Assert.Equal(50001, first.Value!.Port);
        Assert.Equal(50001, cached.Value!.Port);
        Assert.Equal(50002, second.Value!.Port);
    }

    /// <summary>A missing ready file gives a clear error that tells the operator to start the service.</summary>
    [Fact]
    public async Task MissingReadyFile_GivesAnUnavailableError()
    {
        var result = await Locator(new UpstreamEndpointOptions { ReadyFile = Path.Combine(_folder, "none.json") }).LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorKind.UpstreamUnavailable, result.Error!.Kind);
        Assert.Contains("Start xat-rag", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>The upstream services have no authentication, so the locator refuses an address that is not loopback.</summary>
    [Theory]
    [InlineData("http://example.com:8420")]
    [InlineData("http://192.168.1.5:8420")]
    [InlineData("ftp://127.0.0.1:21")]
    public async Task NonLoopbackAddress_IsRefused(string url)
    {
        var result = await Locator(new UpstreamEndpointOptions { BaseUrl = url }).LocateAsync(Upstream.XatRag, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
    }

    /// <summary>The handler sends the request to the located address, sets the host header, removes Origin, and forgets the address after a failed connection.</summary>
    [Fact]
    public async Task AddressHandler_RewritesTheRequest_AndInvalidatesOnFailure()
    {
        var locator = new FakeLocator(new Uri("http://127.0.0.1:50003"));
        var inner = new CapturingHandler();
        var handler = new UpstreamAddressHandler(locator, Upstream.XatRag) { InnerHandler = inner };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://xat-rag.invalid/") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/rags?x=1");
        request.Headers.Add("Origin", "http://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("http://127.0.0.1:50003/v1/rags?x=1", inner.Last!.RequestUri!.ToString());
        Assert.Equal("127.0.0.1:50003", inner.Last.Headers.Host);
        Assert.False(inner.Last.Headers.Contains("Origin"));

        inner.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/v1/rags", TestContext.Current.CancellationToken));
        Assert.Equal(1, locator.Invalidations);
    }

    private sealed class FakeLocator(Uri uri) : IUpstreamLocator
    {
        public int Invalidations { get; private set; }

        public Task<Result<Uri>> LocateAsync(Upstream upstream, CancellationToken ct) => Task.FromResult(Result<Uri>.Ok(uri));

        public void Invalidate(Upstream upstream) => Invalidations++;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Fail
                ? throw new HttpRequestException(HttpRequestError.ConnectionError)
                : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
