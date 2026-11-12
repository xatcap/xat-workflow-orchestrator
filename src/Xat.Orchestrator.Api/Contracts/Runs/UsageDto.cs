namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>The token usage.</summary>
/// <param name="PromptTokens">The tokens of the requests.</param>
/// <param name="CompletionTokens">The tokens of the answers.</param>
internal sealed record UsageDto(int PromptTokens, int CompletionTokens);
