namespace Xat.Orchestrator.Domain.Generation;

/// <summary>The role of the author of a chat message.</summary>
public enum ChatRole
{
    /// <summary>An instruction for the model.</summary>
    System,

    /// <summary>A message from the user.</summary>
    User,

    /// <summary>A message from the model.</summary>
    Assistant,
}
