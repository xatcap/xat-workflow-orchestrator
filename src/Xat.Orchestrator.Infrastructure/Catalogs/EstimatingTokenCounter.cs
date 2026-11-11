using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Catalogs;

/// <summary>
/// Estimates tokens from the length of the text. The engine has no tokenize route, so the orchestrator cannot count exactly.
/// The estimate uses 3 characters for each token. This value is lower than the real ratio for most English text,
/// so the count is higher than the real count and the prompt stays inside the context window.
/// </summary>
internal sealed class EstimatingTokenCounter : ITokenCounter
{
    private const double CharactersPerToken = 3.0;

    /// <inheritdoc/>
    public int Count(ModelProfile model, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (int)Math.Ceiling(text.Length / CharactersPerToken);
    }
}
