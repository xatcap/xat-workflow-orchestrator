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

/// <summary>A fixed model catalog with one model.</summary>
internal sealed class StubModelCatalog : IModelCatalog
{
    private readonly ModelProfile _model = new("default", "local/test-model", 1, ["temperature", "max_tokens"]);

    public string DefaultAlias => "default";

    public Result<ModelProfile> Resolve(string? modelAlias) =>
        string.IsNullOrWhiteSpace(modelAlias) || modelAlias == "default"
            ? Result<ModelProfile>.Ok(_model)
            : Result<ModelProfile>.Fail(new PipelineError("llm.model_unknown", "The model alias is not configured.", ErrorKind.NotFound));
}
