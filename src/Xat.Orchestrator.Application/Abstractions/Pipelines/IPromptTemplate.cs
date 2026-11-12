namespace Xat.Orchestrator.Application.Abstractions.Pipelines;

/// <summary>A prompt template that can be filled with data.</summary>
public interface IPromptTemplate
{
    /// <summary>Fills the template.</summary>
    /// <param name="model">The data. The key <c>input</c> holds the input of the run.</param>
    /// <returns>The text.</returns>
    string Render(IReadOnlyDictionary<string, object?> model);
}
