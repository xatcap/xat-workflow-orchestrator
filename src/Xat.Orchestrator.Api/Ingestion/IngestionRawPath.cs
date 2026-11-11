using Microsoft.AspNetCore.Http.Features;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>
/// Builds the path of a delete-document request from the original text of the request. A document ID can hold a slash.
/// The caller sends it as <c>%2F</c>. The framework keeps this text in the path, and a normal forwarder would encode the percent sign
/// again, so xat-rag would get <c>%252F</c> and a different ID. The original text keeps the ID exact.
/// The original text is not normalised, so the method refuses an ID that holds a dot segment, which could point to another path.
/// </summary>
internal static class IngestionRawPath
{
    /// <summary>Builds the path for xat-rag.</summary>
    /// <param name="http">The HTTP context of the request.</param>
    /// <param name="rag">The name of the RAG, from the route.</param>
    /// <param name="path">The path for xat-rag, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the original text is safe to forward.</returns>
    public static bool TryBuildDeletePath(HttpContext http, string rag, out string path)
    {
        path = string.Empty;
        var raw = http.Features.Get<IHttpRequestFeature>()?.RawTarget;

        // Kestrel always sets the original text. A host that sets none, such as the in-process test server, gets the decoded path.
        // The decoded path cannot tell %2F from %252F, but a server that sets no original text has no better source.
        if (string.IsNullOrEmpty(raw))
        {
            raw = http.Request.Path.Value;
        }

        var prefix = $"/v1/rags/{rag}/documents/";
        if (raw is null || !raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = raw.IndexOf('?', StringComparison.Ordinal);
        var rawId = query < 0 ? raw[prefix.Length..] : raw[prefix.Length..query];
        if (rawId.Length == 0 || HasDotSegment(rawId))
        {
            return false;
        }

        path = prefix + rawId;
        return true;
    }

    /// <summary>Shows whether an ID holds a segment that is a dot or two dots, also when the dots or the separators are encoded.</summary>
    private static bool HasDotSegment(string rawId)
    {
        // Decode the whole text, then split on every kind of separator. A caller can hide a separator or a dot with encoding.
        var decoded = Uri.UnescapeDataString(Uri.UnescapeDataString(rawId));
        return decoded.Split('/', '\\').Any(segment => segment is "." or "..") || rawId.Contains('\\', StringComparison.Ordinal);
    }
}
