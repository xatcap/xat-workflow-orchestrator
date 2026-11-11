namespace Xat.Orchestrator.Domain.Generation;

/// <summary>One message in a chat request to the LLM.</summary>
/// <param name="Role">The role of the author.</param>
/// <param name="Content">The text of the message.</param>
public sealed record ChatMessage(ChatRole Role, string Content);
