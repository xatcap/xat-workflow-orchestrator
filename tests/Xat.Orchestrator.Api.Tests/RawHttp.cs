using System.Net.Sockets;
using System.Text;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>
/// Sends a request over a plain socket. An HTTP client library normalises the path before it sends it, for example it removes dot segments.
/// A test that needs the path to reach the server exactly as written uses this class.
/// </summary>
internal static class RawHttp
{
    /// <summary>Sends a request with no body and reads the status code of the answer.</summary>
    /// <param name="baseAddress">The address of the server.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="rawTarget">The request target, exactly as the test writes it.</param>
    /// <returns>The status code.</returns>
    public static async Task<int> SendAsync(Uri baseAddress, string method, string rawTarget)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(baseAddress.Host, baseAddress.Port, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        var request = $"{method} {rawTarget} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), TestContext.Current.CancellationToken);

        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        var statusLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
        return int.Parse(statusLine.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }
}
