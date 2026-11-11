using System.ComponentModel.DataAnnotations;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Steps.Retrieval;

/// <summary>
/// Merges the chunks of several retrieval steps into one list. The step takes the chunks by rank, one from each source in turn.
/// It does not compare scores, because the scores of different contexts do not have the same scale.
/// </summary>
public sealed class RetrievalMergeStep : IPipelineStep<RetrievalMergeOptions>, IStepValidator<RetrievalMergeOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, RetrievalMergeOptions options, CancellationToken ct)
    {
        var state = context.Run.State;
        var stepId = context.Step.Id;

        var lists = new List<IReadOnlyList<Chunk>>();
        foreach (var source in options.From!)
        {
            if (!state.TryGetOutput(source, out var output)
                || !output.TryGetValue("chunks", out var chunks)
                || chunks is not IReadOnlyList<Chunk> list)
            {
                return ValueTask.FromResult(StepOutcome.Fail(new PipelineError(
                    "merge.source_missing", $"The step '{source}' has no chunks to merge.", ErrorKind.Validation, Source: stepId)));
            }

            lists.Add(list);
        }

        var merged = new List<Chunk>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var longest = lists.Count == 0 ? 0 : lists.Max(list => list.Count);
        for (var rank = 0; rank < longest; rank++)
        {
            foreach (var list in lists)
            {
                if (rank < list.Count && (!options.Dedupe || seen.Add(list[rank].ChunkId)))
                {
                    merged.Add(list[rank]);
                }
            }
        }

        if (options.MaxChunks is { } max && merged.Count > max)
        {
            merged.RemoveRange(max, merged.Count - max);
        }

        state.SetOutput(stepId, new Dictionary<string, object?>
        {
            ["chunks"] = (IReadOnlyList<Chunk>)merged,
            ["count"] = merged.Count,
        });
        state.Prompt.ReplaceChunkSections(
            options.From!.ToList(),
            new PromptSection(stepId, PromptSectionKind.Chunks, null, stepId, Trusted: false));
        return ValueTask.FromResult(StepOutcome.Continue);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(RetrievalMergeOptions options, StepValidationContext context) =>
        options.From!
            .Where(source => !context.EarlierStepIds.Contains(source))
            .Select(source => $"The source '{source}' is not an earlier step.")
            .ToList();
}
