using System.Net;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>Checks that an address is a loopback address. Both upstream services have no authentication.</summary>
internal static class LoopbackUrl
{
    /// <summary>
    /// Parses an http base address and checks that its host is loopback. The adapters send absolute paths, such as
    /// <c>/v1/context</c>, so the address must have no path, no query and no fragment.
    /// </summary>
    /// <param name="text">The address.</param>
    /// <param name="uri">The address, when it is valid.</param>
    /// <returns><see langword="true"/> for an http or https loopback address with no path.</returns>
    public static bool TryParse(string text, out Uri uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && parsed.AbsolutePath == "/"
            && parsed.Query.Length == 0
            && parsed.Fragment.Length == 0
            && IsLoopbackHost(parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsLoopbackHost(Uri uri) =>
        string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.IdnHost, out var address) && IPAddress.IsLoopback(address));
}
