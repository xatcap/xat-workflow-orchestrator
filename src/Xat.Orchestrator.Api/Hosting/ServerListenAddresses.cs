using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xat.Orchestrator.Application.Abstractions.Runs;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>Reads the addresses that the server really bound, also a port that the system picked.</summary>
/// <param name="server">The server.</param>
internal sealed class ServerListenAddresses(IServer server) : IListenAddresses
{
    /// <inheritdoc/>
    public IReadOnlyCollection<Uri> Current =>
        (server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .ToList();
}
