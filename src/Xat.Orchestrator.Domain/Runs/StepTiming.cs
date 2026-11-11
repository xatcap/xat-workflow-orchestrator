namespace Xat.Orchestrator.Domain.Runs;

/// <summary>The time that one step used.</summary>
/// <param name="StepId">The ID of the step.</param>
/// <param name="Uses">The registered name of the step type.</param>
/// <param name="Duration">The time from the start to the end of the step.</param>
public sealed record StepTiming(string StepId, string Uses, TimeSpan Duration);
