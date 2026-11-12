using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions.Llm;

/// <summary>The complete answer of the LLM.</summary>
/// <param name="Text">The text of the answer.</param>
/// <param name="FinishReason">Why the model stopped, for example <c>stop</c> or <c>length</c>. The value can be <see langword="null"/>.</param>
/// <param name="Usage">The token usage, or <see langword="null"/> when the engine did not report it.</param>
public sealed record Completion(string Text, string? FinishReason, TokenUsage? Usage);
