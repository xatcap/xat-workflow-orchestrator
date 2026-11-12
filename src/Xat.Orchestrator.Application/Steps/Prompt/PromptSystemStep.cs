using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.StepContracts;

namespace Xat.Orchestrator.Application.Steps.Prompt;

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
