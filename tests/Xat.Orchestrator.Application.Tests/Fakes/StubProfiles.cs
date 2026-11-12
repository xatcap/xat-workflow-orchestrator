using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>Fixed retrieval profiles: the aliases <c>default</c>, <c>docs</c> and <c>jira</c>.</summary>
internal sealed class StubProfiles : IRetrievalProfiles
{
    private readonly Dictionary<string, RetrievalProfile> _profiles = new()
    {
        ["default"] = new("default", "ask-all", 8),
        ["docs"] = new("docs", "ask-docs", 5),
        ["jira"] = new("jira", "ask-jira", 5),
    };

    public IReadOnlyCollection<RetrievalProfile> All => _profiles.Values;

    public Result<RetrievalProfile> Resolve(string profileAlias) =>
        _profiles.TryGetValue(profileAlias, out var profile)
            ? Result<RetrievalProfile>.Ok(profile)
            : Result<RetrievalProfile>.Fail(new PipelineError("retrieve.profile_unknown", "The profile is not configured.", ErrorKind.Validation));
}
