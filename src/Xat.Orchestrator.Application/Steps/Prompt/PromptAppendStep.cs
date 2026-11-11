using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps.Prompt;

/// <summary>Adds an instruction that the pipeline author wrote. The text is trusted, so the assemble step does not escape it.</summary>
public sealed class PromptAppendStep(IPromptTemplateStore templates) : IPipelineStep<PromptAppendOptions>, IStepValidator<PromptAppendOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, PromptAppendOptions options, CancellationToken ct)
    {
        var text = StepText.Resolve(templates, options.Text, options.Template, context.Run.State);
        if (!text.IsSuccess)
        {
            return ValueTask.FromResult(StepOutcome.Fail(text.Error with { Source = context.Step.Id }));
        }

        if (!string.IsNullOrWhiteSpace(text.Value))
        {
            context.Run.State.Prompt.AddSection(
                new PromptSection(options.Name, PromptSectionKind.Text, text.Value, null, Trusted: true));
        }

        return ValueTask.FromResult(StepOutcome.Continue);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(PromptAppendOptions options, StepValidationContext context) =>
        StepText.Validate(options.Text, options.Template, context.Templates);
}
