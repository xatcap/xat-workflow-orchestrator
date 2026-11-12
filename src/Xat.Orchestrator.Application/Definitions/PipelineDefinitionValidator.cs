using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Definitions;

/// <summary>
/// Checks a pipeline definition when the service starts. An invalid definition stops the start,
/// so a mistake in a pipeline file never shows up in the middle of a request.
/// </summary>
public sealed partial class PipelineDefinitionValidator(
    IStepRegistry registry,
    IRetrievalProfiles profiles,
    IModelCatalog models,
    IPromptTemplateStore templates)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}\\z")]
    private static partial Regex StepIdPattern();

    /// <summary>Checks one pipeline.</summary>
    /// <param name="definition">The pipeline.</param>
    /// <returns>The messages about problems. An empty list means that the pipeline is valid.</returns>
    public IReadOnlyList<string> Validate(PipelineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var messages = new List<string>();
        void Add(string message) => messages.Add($"Pipeline '{definition.Name}': {message}");

        if (!NamePattern().IsMatch(definition.Name))
        {
            Add("the name must match [a-z0-9][a-z0-9-]{0,63}.");
        }

        if (definition.Version < 1)
        {
            Add("the version must be 1 or higher.");
        }

        if (definition.Steps.Count == 0)
        {
            Add("the pipeline has no steps.");
        }

        foreach (var problem in InputSchemaValidator.CheckSchema(definition.InputSchema))
        {
            Add(problem);
        }

        var earlier = new HashSet<string>(StringComparer.Ordinal);
        var producesAnswer = false;
        var streams = false;
        foreach (var step in definition.Steps)
        {
            if (!StepIdPattern().IsMatch(step.Id))
            {
                Add($"the step ID '{step.Id}' must match [a-z0-9][a-z0-9_-]{{0,63}}.");
            }

            if (earlier.Contains(step.Id))
            {
                Add($"the step ID '{step.Id}' is used more than once.");
            }

            var registration = registry.Find(step.Uses);
            if (registration is null)
            {
                Add($"step '{step.Id}' uses the unknown step type '{step.Uses}'.");
            }
            else
            {
                producesAnswer |= registration.Traits.HasFlag(StepTraits.ProducesAnswer);
                streams |= registration.Traits.HasFlag(StepTraits.Streams);

                var context = new StepValidationContext(profiles, models, templates, earlier);
                messages.AddRange(registration.Validate(step.With, context).Select(problem => $"Pipeline '{definition.Name}': step '{step.Id}': {problem}"));
            }

            messages.AddRange(ExpressionResolver.FindPaths(step.With)
                .Select(path => (Path: path, Reason: ExpressionPath.Check(path, earlier)))
                .Where(check => check.Reason is not null)
                .Select(check => $"Pipeline '{definition.Name}': step '{step.Id}': the expression path '{check.Path}' {check.Reason}."));

            earlier.Add(step.Id);
        }

        if (definition.Steps.Count > 0 && !producesAnswer)
        {
            Add("no step produces an answer.");
        }

        if (definition.Stream == StreamMode.Required && !streams)
        {
            Add("the stream mode is 'required', but no step can stream.");
        }

        return messages;
    }
}
