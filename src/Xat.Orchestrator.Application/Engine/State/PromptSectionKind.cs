namespace Xat.Orchestrator.Application.Engine.State;

/// <summary>The kind of a prompt section.</summary>
public enum PromptSectionKind
{
    /// <summary>A section that holds text.</summary>
    Text,

    /// <summary>A section that holds the chunks of one retrieval step.</summary>
    Chunks,
}
