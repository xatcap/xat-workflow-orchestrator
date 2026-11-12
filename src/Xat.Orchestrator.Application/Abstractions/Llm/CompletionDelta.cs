using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions.Llm;

/// <summary>One part of a streamed answer.</summary>
/// <param name="Text">The new text. The text can be empty.</param>
/// <param name="FinishReason">The reason to stop, or <see langword="null"/> while the model writes.</param>
/// <param name="Usage">The token usage, or <see langword="null"/>. The last part can hold it.</param>
public sealed record CompletionDelta(string Text, string? FinishReason, TokenUsage? Usage);
