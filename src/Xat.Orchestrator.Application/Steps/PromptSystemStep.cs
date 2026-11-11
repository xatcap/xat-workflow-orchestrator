using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>Sets the system prompt of the run.</summary>
public sealed class PromptSystemStep(IPromptTemplateStore templates) : IPipelineStep<PromptSystemOptions>, IStepValidator<PromptSystemOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, PromptSystemOptions options, CancellationToken ct)
    {
        var text = StepText.Resolve(templates, options.Text, options.Template, context.Run.State);
        if (!text.IsSuccess)
        {
            return ValueTask.FromResult(StepOutcome.Fail(text.Error with { Source = context.Step.Id }));
        }

        if (!string.IsNullOrWhiteSpace(text.Value))
        {
            context.Run.State.Prompt.System = text.Value;
        }

        return ValueTask.FromResult(StepOutcome.Continue);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(PromptSystemOptions options, StepValidationContext context) =>
        StepText.Validate(options.Text, options.Template, context.Templates);
}
