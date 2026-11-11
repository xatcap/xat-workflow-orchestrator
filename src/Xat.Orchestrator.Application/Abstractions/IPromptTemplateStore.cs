using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>The store of versioned prompt templates.</summary>
public interface IPromptTemplateStore
{
    /// <summary>Finds a template.</summary>
    /// <param name="reference">The reference in the form <c>name@version</c>.</param>
    /// <returns>The template, or a not-found error.</returns>
    Result<IPromptTemplate> Find(string reference);
}
