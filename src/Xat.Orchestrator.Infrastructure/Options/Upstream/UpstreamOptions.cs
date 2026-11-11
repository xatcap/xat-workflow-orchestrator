using System.ComponentModel.DataAnnotations;

namespace Xat.Orchestrator.Infrastructure.Options.Upstream;

/// <summary>The settings of the two upstream services.</summary>
internal sealed class UpstreamOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Upstreams";

    /// <summary>Gets the settings of xat-rag.</summary>
    public UpstreamEndpointOptions XatRag { get; init; } = new();

    /// <summary>Gets the settings of xat-llm-engine.</summary>
    public UpstreamEndpointOptions XatLlmEngine { get; init; } = new() { BaseUrl = "http://127.0.0.1:8420" };
}
