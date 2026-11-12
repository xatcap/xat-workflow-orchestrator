using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps.Retrieval;

/// <summary>Asks one xat-rag context for the chunks that match a question. A pipeline can use this step more than once.</summary>
public sealed class RetrieveStep(IRetrievalClient retrieval, IRetrievalProfiles profiles) : IPipelineStep<RetrieveOptions>, IStepValidator<RetrieveOptions>
{
    /// <summary>The highest question length that xat-rag accepts.</summary>
    internal const int MaxQueryLength = 20000;

    /// <inheritdoc/>
    public async ValueTask<StepOutcome> ExecuteAsync(StepContext context, RetrieveOptions options, CancellationToken ct)
    {
        var state = context.Run.State;
        var stepId = context.Step.Id;

        var profile = profiles.Resolve(options.Profile!);
        if (!profile.IsSuccess)
        {
            return StepOutcome.Fail(profile.Error with { Source = stepId });
        }

        var query = options.Query ?? state.Prompt.User;
        if (string.IsNullOrWhiteSpace(query))
        {
            return StepOutcome.Fail(new PipelineError(
                "retrieve.no_query", "The step has no question. Set the prompt of the caller or the query option.", ErrorKind.Validation, Source: stepId));
        }

        if (query.Length > MaxQueryLength)
        {
            return StepOutcome.Fail(new PipelineError(
                "retrieve.query_too_long", $"The question is longer than {MaxQueryLength} characters.", ErrorKind.Validation, Source: stepId));
        }

        var result = await retrieval.RetrieveAsync(
            new RetrievalQuery(profile.Value.Context, query, options.Chunks ?? profile.Value.Chunks), ct);
        if (!result.IsSuccess)
        {
            return StepOutcome.Fail(result.Error with { Source = result.Error.Source ?? stepId });
        }

        state.SetOutput(stepId, StepOutputs.Chunks, result.Value.Chunks);
        state.SetOutput(stepId, StepOutputs.Count, result.Value.Chunks.Count);
        state.SetOutput(stepId, StepOutputs.Omitted, result.Value.OmittedCount);
        state.Prompt.AddSection(PromptSection.ForChunks(stepId));
        return StepOutcome.Continue;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(RetrieveOptions options, StepValidationContext context)
    {
        if (options.Profile is null || ExpressionResolver.IsExpression(options.Profile))
        {
            return [];
        }

        // The alias comes from the pipeline file here, not from a caller, so the message can name it.
        var profile = context.Profiles.Resolve(options.Profile);
        return profile.IsSuccess ? [] : [$"{profile.Error.Message} The alias is '{options.Profile}'."];
    }
}
