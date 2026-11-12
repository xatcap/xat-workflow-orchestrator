using System.Text;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Steps.Llm;

/// <summary>
/// Sends the chat messages to the LLM and stores the answer. When the caller reads a stream, the step sends each new part
/// of the answer as an event while the model writes it.
/// </summary>
public sealed class LlmGenerateStep(ILlmClient llm, IModelCatalog models) : IPipelineStep<LlmGenerateOptions>, IStepValidator<LlmGenerateOptions>
{
    /// <inheritdoc/>
    public async ValueTask<StepOutcome> ExecuteAsync(StepContext context, LlmGenerateOptions options, CancellationToken ct)
    {
        var state = context.Run.State;
        var stepId = context.Step.Id;

        var model = models.Resolve(options.Model);
        if (!model.IsSuccess)
        {
            return StepOutcome.Fail(model.Error with { Source = stepId });
        }

        if (!state.TryGet(StateKeys.Messages, out var messages))
        {
            return StepOutcome.Fail(new PipelineError(
                "llm.no_messages", "The run has no chat messages. Add a prompt.assemble step before this step.", ErrorKind.Validation, Source: stepId));
        }

        var request = new LlmRequest(model.Value, messages, options.Temperature, options.MaxTokens);
        string text;
        string? finishReason;
        TokenUsage? usage;

        if (context.Run.Events.Streaming)
        {
            var builder = new StringBuilder();
            finishReason = null;
            usage = null;
            await foreach (var item in llm.StreamAsync(request, ct))
            {
                if (!item.IsSuccess)
                {
                    return StepOutcome.Fail(item.Error with { Source = item.Error.Source ?? stepId });
                }

                if (item.Value.Text.Length > 0)
                {
                    builder.Append(item.Value.Text);
                    await context.Run.Events.EmitAsync(new RunEvent.Delta(item.Value.Text), ct);
                }

                finishReason = item.Value.FinishReason ?? finishReason;
                usage = item.Value.Usage ?? usage;
            }

            text = builder.ToString();
        }
        else
        {
            var completion = await llm.CompleteAsync(request, ct);
            if (!completion.IsSuccess)
            {
                return StepOutcome.Fail(completion.Error with { Source = completion.Error.Source ?? stepId });
            }

            text = completion.Value.Text;
            finishReason = completion.Value.FinishReason;
            usage = completion.Value.Usage;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return StepOutcome.Fail(new PipelineError(
                "llm.empty_answer", "The model returned an empty answer.", ErrorKind.UpstreamFailed, Source: stepId));
        }

        if (string.Equals(finishReason, "length", StringComparison.Ordinal))
        {
            state.AddWarning("The answer stopped because it reached the token limit.");
        }

        state.AddUsage(usage ?? TokenUsage.None);
        state.Set(StateKeys.Answer, new Answer(text, []));
        return StepOutcome.Continue;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(LlmGenerateOptions options, StepValidationContext context)
    {
        if (options.Model is null || ExpressionResolver.IsExpression(options.Model))
        {
            return [];
        }

        // The alias comes from the pipeline file here, not from a caller, so the message can name it.
        var model = context.Models.Resolve(options.Model);
        return model.IsSuccess ? [] : [$"{model.Error.Message} The alias is '{options.Model}'."];
    }
}
