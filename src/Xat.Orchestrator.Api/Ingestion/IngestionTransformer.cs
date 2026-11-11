using Yarp.ReverseProxy.Forwarder;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>
/// Prepares the request to xat-rag. xat-rag refuses a request that has an <c>Origin</c> header or a <c>Host</c> header that is not loopback.
/// The orchestrator already refuses a request with an <c>Origin</c> header, so the transformer sets only the <c>Host</c> header.
/// It lets only a short list of headers through. So credentials, cookies and headers that change the meaning of a request never reach xat-rag.
/// </summary>
/// <param name="destination">The base address of xat-rag.</param>
/// <param name="pathOverride">A path and query that replaces the normal path, or <see langword="null"/>. The delete-document route uses it to keep the encoding of the document ID.</param>
internal sealed class IngestionTransformer(Uri destination, string? pathOverride = null) : HttpTransformer
{
    // Only these request headers go on to xat-rag. The body headers (Content-Type and Content-Length) are not in this list, because they belong to the content.
    // A list of allowed names is safer than a list of removed names: a header such as X-HTTP-Method-Override or X-Forwarded-For never reaches xat-rag.
    private static readonly HashSet<string> _allowedHeaders = new(["Accept", "Accept-Encoding", "User-Agent"], StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix,
        CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

        foreach (var name in proxyRequest.Headers.Select(header => header.Key).Where(name => !_allowedHeaders.Contains(name)).ToList())
        {
            proxyRequest.Headers.Remove(name);
        }

        proxyRequest.Headers.Host = destination.Authority;
        if (pathOverride is not null)
        {
            proxyRequest.RequestUri = new Uri(destination, pathOverride);
        }
    }
}
