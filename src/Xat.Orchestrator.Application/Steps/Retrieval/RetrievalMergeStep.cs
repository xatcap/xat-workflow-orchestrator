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
            if (!state.TryGetOutput(source, StepOutputs.Chunks, out var list))
            {
                return ValueTask.FromResult(StepOutcome.Fail(new PipelineError(
                    "merge.source_missing", $"The step '{source}' has no chunks to merge.", ErrorKind.Validation, Source: stepId)));
            }

            lists.Add(list);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = InRankOrder(lists)
            .Where(chunk => !options.Dedupe || seen.Add(chunk.ChunkId))
            .Take(options.MaxChunks ?? int.MaxValue)
            .ToList();

        state.SetOutput<IReadOnlyList<Chunk>>(stepId, StepOutputs.Chunks, merged);
        state.SetOutput(stepId, StepOutputs.Count, merged.Count);
        state.Prompt.ReplaceChunkSections(options.From!.ToList(), PromptSection.ForChunks(stepId));
        return ValueTask.FromResult(StepOutcome.Continue);
    }

    /// <summary>Gives the first chunk of each list, then the second chunk of each list, and so on.</summary>
    private static IEnumerable<Chunk> InRankOrder(List<IReadOnlyList<Chunk>> lists)
    {
        var longest = lists.Count == 0 ? 0 : lists.Max(list => list.Count);
        return Enumerable.Range(0, longest)
            .SelectMany(rank => lists.Where(list => rank < list.Count).Select(list => list[rank]));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(RetrievalMergeOptions options, StepValidationContext context) =>
        options.From!
            .Where(source => !context.EarlierStepIds.Contains(source))
            .Select(source => $"The source '{source}' is not an earlier step.")
            .ToList();
}
