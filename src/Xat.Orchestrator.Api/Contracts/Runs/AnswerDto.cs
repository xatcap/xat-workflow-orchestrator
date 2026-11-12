namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The answer text and its citations.</summary>
/// <param name="Text">The text.</param>
/// <param name="Citations">The cited sources.</param>
internal sealed record AnswerDto(string Text, IReadOnlyList<CitationDto> Citations);
