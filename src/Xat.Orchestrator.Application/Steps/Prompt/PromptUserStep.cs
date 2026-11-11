using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps.Prompt;

/// <summary>Sets the prompt of the caller.</summary>
public sealed class PromptUserStep : IPipelineStep<PromptUserOptions>, IStepValidator<PromptUserOptions>
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(PromptUserOptions options, StepValidationContext context) =>
        options.Text is null ? ["Set the 'text' option."] : [];

    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, PromptUserOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Text))
        {
            return ValueTask.FromResult(StepOutcome.Fail(new PipelineError(
                "prompt.user_empty", "The prompt of the caller is empty.", ErrorKind.Validation, Source: context.Step.Id)));
        }

        context.Run.State.Prompt.User = options.Text;
        return ValueTask.FromResult(StepOutcome.Continue);
    }
}
