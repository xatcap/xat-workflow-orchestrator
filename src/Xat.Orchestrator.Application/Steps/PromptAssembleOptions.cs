namespace Xat.Orchestrator.Application.Steps;

/// <summary>The options of <see cref="PromptAssembleStep"/>.</summary>
public sealed class PromptAssembleOptions
{
    /// <summary>
    /// Gets the names of the sections in the order that the prompt uses them. A section that the list does not name goes after
    /// the named sections, in the order in which the steps added it.
    /// </summary>
    public IReadOnlyList<string>? Order { get; init; }
}
