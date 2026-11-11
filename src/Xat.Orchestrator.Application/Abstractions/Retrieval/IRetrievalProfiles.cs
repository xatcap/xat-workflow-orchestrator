using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Abstractions.Retrieval;

/// <summary>The configured retrieval aliases. An alias points to a xat-rag context.</summary>
public interface IRetrievalProfiles
{
    /// <summary>Gets all retrieval profiles.</summary>
    IReadOnlyCollection<RetrievalProfile> All { get; }

    /// <summary>Finds a retrieval profile.</summary>
    /// <param name="profileAlias">The alias.</param>
    /// <returns>The profile, or a not-found error.</returns>
    Result<RetrievalProfile> Resolve(string profileAlias);
}
