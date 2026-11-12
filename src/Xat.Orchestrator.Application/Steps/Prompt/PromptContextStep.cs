using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;

namespace Xat.Orchestrator.Application.Steps.Prompt;

/// <summary>Adds the context that the caller sent. The assemble step escapes it and puts it in a tag.</summary>
public sealed class PromptContextStep : IPipelineStep<PromptContextOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, PromptContextOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Text))
        {
            context.Run.State.Prompt.AddSection(
                new PromptSection(options.Label, PromptSectionKind.Text, options.Text, null, Trusted: false));
        }

        return ValueTask.FromResult(StepOutcome.Continue);
    }
}
