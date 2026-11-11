using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>Checks that an address is a loopback address. Both upstream services have no authentication.</summary>
internal static class LoopbackUrl
{
    /// <summary>Parses an http address and checks that its host is loopback.</summary>
    /// <param name="text">The address.</param>
    /// <param name="uri">The address, when it is valid.</param>
    /// <returns><see langword="true"/> for an http or https loopback address.</returns>
    public static bool TryParse(string text, out Uri uri)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && (string.Equals(parsed.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(parsed.IdnHost, out var address) && IPAddress.IsLoopback(address))))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
