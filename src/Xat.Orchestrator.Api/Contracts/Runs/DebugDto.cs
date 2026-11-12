namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The debug data of a run.</summary>
/// <param name="Steps">The time of each step.</param>
internal sealed record DebugDto(IReadOnlyList<StepTimingDto> Steps);
