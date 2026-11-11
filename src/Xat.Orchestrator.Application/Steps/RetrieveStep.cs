using System.ComponentModel.DataAnnotations;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps;

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
            // The alias can come from the input of the caller. So the message does not repeat it, and the class is Validation.
            return StepOutcome.Fail(new PipelineError(
                "retrieve.profile_unknown",
                "The retrieval profile is not configured. Use GET /v1/contexts to list the profiles.",
                ErrorKind.Validation,
                Source: stepId));
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

        state.SetOutput(stepId, new Dictionary<string, object?>
        {
            ["chunks"] = result.Value.Chunks,
            ["count"] = result.Value.Chunks.Count,
            ["omitted"] = result.Value.OmittedCount,
        });
        state.Prompt.AddSection(new PromptSection(stepId, PromptSectionKind.Chunks, null, stepId, Trusted: false));
        return StepOutcome.Continue;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(RetrieveOptions options, StepValidationContext context)
    {
        if (options.Profile is null || options.Profile.Contains("${", StringComparison.Ordinal))
        {
            return [];
        }

        var profile = context.Profiles.Resolve(options.Profile);
        return profile.IsSuccess ? [] : [profile.Error.Message];
    }
}
