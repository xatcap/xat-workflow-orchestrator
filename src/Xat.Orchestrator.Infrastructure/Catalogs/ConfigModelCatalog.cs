using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Infrastructure.Options.Llm;

namespace Xat.Orchestrator.Infrastructure.Catalogs;

/// <summary>The model aliases from the configuration. A new model is a new configuration entry, with no code change.</summary>
internal sealed class ConfigModelCatalog : IModelCatalog
{
    private readonly Dictionary<string, ModelProfile> _models;

    /// <summary>Initializes a new instance of the <see cref="ConfigModelCatalog"/> class.</summary>
    /// <param name="options">The model options.</param>
    public ConfigModelCatalog(IOptions<LlmOptions> options)
    {
        DefaultAlias = options.Value.DefaultModel;
        _models = options.Value.Models.ToDictionary(
            entry => entry.Key,
            entry => new ModelProfile(
                entry.Key,
                entry.Value.Model,
                entry.Value.Parallel,
                entry.Value.AllowedFields.ToList()),
            StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public string DefaultAlias { get; }

    /// <inheritdoc/>
    public Result<ModelProfile> Resolve(string? modelAlias)
    {
        var alias = string.IsNullOrWhiteSpace(modelAlias) ? DefaultAlias : modelAlias;
        return _models.TryGetValue(alias, out var model)
            ? Result<ModelProfile>.Ok(model)
            : Result<ModelProfile>.Fail(new PipelineError(
                "llm.model_unknown",
                $"The model alias '{alias}' is not configured. Add it to {LlmOptions.SectionName}:Models.",
                ErrorKind.NotFound));
    }
}
