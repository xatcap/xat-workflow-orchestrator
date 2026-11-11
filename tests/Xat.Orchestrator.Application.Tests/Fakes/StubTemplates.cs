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

/// <summary>A template store with one template, <c>rules@1</c>. The template writes the input value <c>language</c>.</summary>
internal sealed class StubTemplates : IPromptTemplateStore
{
    public Result<IPromptTemplate> Find(string reference) =>
        reference == "rules@1"
            ? Result<IPromptTemplate>.Ok(new RulesTemplate())
            : Result<IPromptTemplate>.Fail(new PipelineError("template.unknown", "The template does not exist.", ErrorKind.NotFound));

    private sealed class RulesTemplate : IPromptTemplate
    {
        public string Render(IReadOnlyDictionary<string, object?> model)
        {
            var input = model["input"] as IReadOnlyDictionary<string, object?>;
            var language = input is not null && input.TryGetValue("language", out var value) ? value as string : null;
            return language is null ? "Be brief." : $"Be brief. Write in {language}.";
        }
    }
}
