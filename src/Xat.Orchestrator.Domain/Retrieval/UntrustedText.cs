namespace Xat.Orchestrator.Domain.Retrieval;

/// <summary>
/// Text that came from a document store. The text can hold instructions that an attacker wrote.
/// Only the prompt assembly step can turn this text into prompt content, and it escapes the text first.
/// </summary>
/// <param name="Value">The raw text.</param>
public readonly record struct UntrustedText(string Value)
{
    /// <summary>Gets a fixed text, so that a log line never shows the raw text.</summary>
    /// <returns>The text <c>[untrusted text]</c>.</returns>
    public override string ToString() => "[untrusted text]";
}
