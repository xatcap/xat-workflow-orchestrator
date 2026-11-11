using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The result of a successful run.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Pipeline">The name of the pipeline.</param>
/// <param name="Version">The version of the pipeline definition.</param>
/// <param name="Status">Shows how the run ended.</param>
/// <param name="Answer">The answer.</param>
/// <param name="Usage">The tokens that the LLM calls used.</param>
/// <param name="Steps">The time of each step.</param>
/// <param name="Warnings">Notes about the run, for example a citation that the sources do not hold.</param>
public sealed record RunResult(
    string RunId,
    string Pipeline,
    int Version,
    RunStatus Status,
    Answer Answer,
    TokenUsage Usage,
    IReadOnlyList<StepTiming> Steps,
    IReadOnlyList<string> Warnings);
