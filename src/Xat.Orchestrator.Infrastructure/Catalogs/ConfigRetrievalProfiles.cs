using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Infrastructure.Options.Retrieval;

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
    /// <remarks>The alias can come from the input of the caller, so the error does not repeat it.</remarks>
    public Result<RetrievalProfile> Resolve(string profileAlias) =>
        _profiles.TryGetValue(profileAlias, out var profile)
            ? Result<RetrievalProfile>.Ok(profile)
            : Result<RetrievalProfile>.Fail(new PipelineError(
                "retrieve.profile_unknown",
                $"The retrieval profile is not configured. Add it to {RetrievalOptions.SectionName}:Profiles.",
                ErrorKind.Validation));
}
