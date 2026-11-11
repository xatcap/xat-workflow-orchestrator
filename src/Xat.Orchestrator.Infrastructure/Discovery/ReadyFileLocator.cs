using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options.Upstream;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>
/// Finds an upstream by a fixed base URL or by its ready file. xat-rag writes a new port into its ready file at each start,
/// so the locator reads the file again after a failed connection.
/// </summary>
internal sealed class ReadyFileLocator(IOptions<UpstreamOptions> options) : IUpstreamLocator
{
    private readonly ConcurrentDictionary<Upstream, Uri> _cache = new();

    /// <inheritdoc/>
    public async Task<Result<Uri>> LocateAsync(Upstream upstream, CancellationToken ct)
    {
        if (_cache.TryGetValue(upstream, out var cached))
        {
            return Result<Uri>.Ok(cached);
        }

        var name = UpstreamNames.Of(upstream);
        var endpoint = upstream == Upstream.XatRag ? options.Value.XatRag : options.Value.XatLlmEngine;
        string? text = endpoint.BaseUrl;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (string.IsNullOrWhiteSpace(endpoint.ReadyFile) || !File.Exists(endpoint.ReadyFile))
            {
                return Unknown(name, $"The ready file of {name} does not exist. Start {name}.");
            }

            try
            {
                var json = await File.ReadAllTextAsync(endpoint.ReadyFile, ct);
                using var document = JsonDocument.Parse(json);
                text = document.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return Unknown(name, $"The ready file of {name} cannot be read.");
            }
        }

        if (text is null || !LoopbackUrl.TryParse(text, out var uri))
        {
            return Unknown(name, $"The address of {name} is not a loopback http address.");
        }

        _cache[upstream] = uri;
        return Result<Uri>.Ok(uri);
    }

    /// <inheritdoc/>
    public void Invalidate(Upstream upstream) => _cache.TryRemove(upstream, out _);

    private static Result<Uri> Unknown(string name, string message) =>
        Result<Uri>.Fail(new PipelineError($"{name}.not_located", message, ErrorKind.UpstreamUnavailable, Source: name));
}
