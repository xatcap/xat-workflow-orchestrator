using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>Counts the tokens of a text for a model. The count of xat-rag is not an LLM token count.</summary>
public interface ITokenCounter
{
    /// <summary>Counts the tokens of a text.</summary>
    /// <param name="model">The model profile that names the tokenizer.</param>
    /// <param name="text">The text.</param>
    /// <returns>The number of tokens. An estimate must not be lower than the real count.</returns>
    int Count(ModelProfile model, string text);
}
