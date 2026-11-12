using System.Collections.Concurrent;
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
        var (text, problem) = string.IsNullOrWhiteSpace(endpoint.BaseUrl)
            ? await ReadUrlAsync(endpoint.ReadyFile, name, ct)
            : (endpoint.BaseUrl, null);

        if (problem is not null)
        {
            return Unknown(name, problem);
        }

        if (text is null || !LoopbackUrl.TryParse(text, out var uri))
        {
            return Unknown(name, $"The address of {name} is not a loopback http or https address with no path.");
        }

        _cache[upstream] = uri;
        return Result<Uri>.Ok(uri);
    }

    /// <inheritdoc/>
    public void Invalidate(Upstream upstream) => _cache.TryRemove(upstream, out _);

    /// <summary>Reads the <c>url</c> field of a ready file. A file with a different shape counts as a file that cannot be read.</summary>
    private static async Task<(string? Url, string? Problem)> ReadUrlAsync(string? path, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (null, $"The ready file of {name} does not exist. Start {name}.");
        }

        var unreadable = $"The ready file of {name} cannot be read.";
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var hasUrl = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("url", out var url)
                && url.ValueKind == JsonValueKind.String;
            return hasUrl ? (root.GetProperty("url").GetString(), null) : (null, unreadable);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, unreadable);
        }
    }

    private static Result<Uri> Unknown(string name, string message) =>
        Result<Uri>.Fail(new PipelineError($"{name}.not_located", message, ErrorKind.UpstreamUnavailable, Source: name));
}
