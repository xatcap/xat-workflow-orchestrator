using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Catalogs;

/// <summary>The retrieval aliases from the configuration. A new context is a new configuration entry, with no code change.</summary>
internal sealed class ConfigRetrievalProfiles : IRetrievalProfiles
{
    private readonly Dictionary<string, RetrievalProfile> _profiles;

    /// <summary>Initializes a new instance of the <see cref="ConfigRetrievalProfiles"/> class.</summary>
    /// <param name="options">The retrieval options.</param>
    public ConfigRetrievalProfiles(IOptions<RetrievalOptions> options)
    {
        _profiles = options.Value.Profiles.ToDictionary(
            entry => entry.Key,
            entry => new RetrievalProfile(entry.Key, entry.Value.Context, entry.Value.Chunks),
            StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<RetrievalProfile> All => _profiles.Values;

    /// <inheritdoc/>
    public Result<RetrievalProfile> Resolve(string profileAlias) =>
        _profiles.TryGetValue(profileAlias, out var profile)
            ? Result<RetrievalProfile>.Ok(profile)
            : Result<RetrievalProfile>.Fail(new PipelineError(
                "retrieval.profile_unknown",
                $"The retrieval profile '{profileAlias}' is not configured. Add it to {RetrievalOptions.SectionName}:Profiles.",
                ErrorKind.NotFound));
}
