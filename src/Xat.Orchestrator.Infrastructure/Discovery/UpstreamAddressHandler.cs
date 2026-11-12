namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>
/// Sends each request to the current address of an upstream. The handler sits inside the retry handler,
/// so each retry finds the address again. It also removes the <c>Origin</c> header, which xat-rag refuses.
/// </summary>
internal sealed class UpstreamAddressHandler(IUpstreamLocator locator, Upstream upstream) : DelegatingHandler
{
    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var located = await locator.LocateAsync(upstream, cancellationToken);
        if (!located.IsSuccess)
        {
            throw new UpstreamNotLocatedException(located.Error);
        }

        var path = request.RequestUri?.PathAndQuery ?? "/";
        request.RequestUri = new Uri(located.Value, path);
        request.Headers.Host = located.Value.Authority;
        request.Headers.Remove("Origin");

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // The service can have a new address after a restart. Read the address again at the next try.
            locator.Invalidate(upstream);
            throw;
        }
    }
}
