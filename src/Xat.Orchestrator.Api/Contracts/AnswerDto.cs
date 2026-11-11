using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The answer text and its citations.</summary>
/// <param name="Text">The text.</param>
/// <param name="Citations">The cited sources.</param>
internal sealed record AnswerDto(string Text, IReadOnlyList<CitationDto> Citations);
