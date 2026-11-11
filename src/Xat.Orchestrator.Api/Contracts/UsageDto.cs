using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The token usage.</summary>
/// <param name="PromptTokens">The tokens of the requests.</param>
/// <param name="CompletionTokens">The tokens of the answers.</param>
internal sealed record UsageDto(int PromptTokens, int CompletionTokens);
