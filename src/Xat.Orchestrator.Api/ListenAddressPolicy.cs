using System.Net;

namespace Xat.Orchestrator.Api;

/// <summary>
/// Refuses a listen address that is not loopback. The orchestrator has no authentication,
/// so a network address would give every machine on the network access to it.
/// </summary>
internal static class ListenAddressPolicy
{
    /// <summary>Checks that each listen address is a loopback address or a Unix socket.</summary>
    /// <param name="urls">The listen addresses, for example <c>http://127.0.0.1:8430</c>.</param>
    /// <exception cref="InvalidOperationException">An address is not loopback.</exception>
    public static void EnsureLoopbackOnly(IEnumerable<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);

        foreach (var url in urls)
        {
            if (!IsLoopback(url))
            {
                throw new InvalidOperationException(
                    "The orchestrator has no authentication, so it must listen on a loopback address only. Use 127.0.0.1, [::1] or localhost.");
            }
        }
    }

    /// <summary>Splits the value of the <c>urls</c> setting into single addresses.</summary>
    /// <param name="urls">The setting value. Semicolons separate the addresses. The value can be <see langword="null"/>.</param>
    /// <returns>The addresses, or an empty list when the setting is not set.</returns>
    public static IReadOnlyList<string> SplitUrls(string? urls) =>
        string.IsNullOrWhiteSpace(urls)
            ? []
            : urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Checks one listen address.</summary>
    /// <param name="url">The listen address.</param>
    /// <returns><see langword="true"/> for a loopback address or a Unix socket.</returns>
    internal static bool IsLoopback(string url)
    {
        // A Unix socket has no network address. The file system controls access to it.
        if (url.StartsWith("http://unix:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The wildcard forms ("+", "*", "0.0.0.0" and "[::]") are not valid URIs or are not loopback.
        // Uri.TryCreate rejects the first two, and IPAddress.IsLoopback rejects the others.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.IdnHost;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
