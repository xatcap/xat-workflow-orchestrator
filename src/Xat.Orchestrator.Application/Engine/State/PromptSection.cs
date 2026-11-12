namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>One part of the prompt that a step added. The assemble step turns all parts into chat messages.</summary>
/// <param name="Name">The name of the section. A pipeline can list the names to set the order.</param>
/// <param name="Kind">The kind of the section.</param>
/// <param name="Text">The text, for a text section.</param>
/// <param name="SourceStepId">The ID of the retrieval step that holds the chunks, for a chunk section.</param>
/// <param name="Trusted">Shows whether the pipeline author wrote the text. Trusted text is not escaped.</param>
public sealed record PromptSection(string Name, PromptSectionKind Kind, string? Text, string? SourceStepId, bool Trusted)
{
    /// <summary>
    /// Makes the chunk section of a step. The name and the source are the ID of the step, so a later merge step can find
    /// and replace the section, and the assemble step can read the chunks of the step.
    /// </summary>
    /// <param name="stepId">The ID of the step that holds the chunks.</param>
    /// <returns>The section.</returns>
    public static PromptSection ForChunks(string stepId) => new(stepId, PromptSectionKind.Chunks, null, stepId, Trusted: false);
}
