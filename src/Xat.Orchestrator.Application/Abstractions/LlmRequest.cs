using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>A chat request for the LLM.</summary>
/// <param name="Model">The model profile. The adapter sends only the fields that the profile allows.</param>
/// <param name="Messages">The messages.</param>
/// <param name="Temperature">The sampling temperature, or <see langword="null"/> for the default of the engine.</param>
/// <param name="MaxTokens">The highest number of tokens in the answer, or <see langword="null"/>.</param>
public sealed record LlmRequest(
    ModelProfile Model,
    IReadOnlyList<ChatMessage> Messages,
    double? Temperature,
    int? MaxTokens);
