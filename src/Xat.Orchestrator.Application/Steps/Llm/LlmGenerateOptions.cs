using System.ComponentModel.DataAnnotations;
using System.Text;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Steps.Llm;

/// <summary>The options of <see cref="LlmGenerateStep"/>.</summary>
public sealed class LlmGenerateOptions
{
    /// <summary>Gets the alias of the model. The default is the default alias.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the sampling temperature. The default is the default of the engine.</summary>
    [Range(0.0, 2.0)]
    public double? Temperature { get; init; }

    /// <summary>Gets the highest number of tokens in the answer. The default is the default of the engine.</summary>
    [Range(1, 1_000_000)]
    public int? MaxTokens { get; init; }
}
