using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>The configured model aliases.</summary>
public interface IModelCatalog
{
    /// <summary>Gets the alias that is used when a step names no model.</summary>
    string DefaultAlias { get; }

    /// <summary>Finds a model profile.</summary>
    /// <param name="modelAlias">The alias, or <see langword="null"/> for the default alias.</param>
    /// <returns>The profile, or a not-found error.</returns>
    Result<ModelProfile> Resolve(string? modelAlias);
}
