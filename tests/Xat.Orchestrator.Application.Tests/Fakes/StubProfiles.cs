using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
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
            : Result<RetrievalProfile>.Fail(new PipelineError("retrieval.profile_unknown", "The profile is not configured.", ErrorKind.NotFound));
}
