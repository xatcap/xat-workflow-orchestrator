namespace Xat.Orchestrator.Domain.Generation;

/// <summary>The answer that a pipeline returns to the caller.</summary>
/// <param name="Text">The text of the answer.</param>
/// <param name="Citations">The sources that the answer cites. The list can be empty.</param>
public sealed record Answer(string Text, IReadOnlyList<Citation> Citations);
