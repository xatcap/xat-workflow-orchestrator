namespace Xat.Orchestrator.Infrastructure.Tests.Support;

/// <summary>A client factory that gives a new client over one fixed handler, for adapter tests without a service provider.</summary>
internal sealed class SingleClientFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
}
