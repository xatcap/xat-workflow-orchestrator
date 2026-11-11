namespace Xat.Orchestrator.Application.Engine;

/// <summary>The prompt that the steps of a run build. The steps only add parts. The assemble step does the work.</summary>
public sealed class PromptDraft
{
    private readonly List<PromptSection> _sections = [];

    /// <summary>Gets or sets the system prompt, or <see langword="null"/> when no step set it.</summary>
    public string? System { get; set; }

    /// <summary>Gets or sets the prompt of the caller, or <see langword="null"/> when no step set it.</summary>
    public string? User { get; set; }

    /// <summary>Gets the sections in the order in which the steps added them.</summary>
    public IReadOnlyList<PromptSection> Sections => _sections;

    /// <summary>Adds a section at the end.</summary>
    /// <param name="section">The section.</param>
    public void AddSection(PromptSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        _sections.Add(section);
    }

    /// <summary>Replaces the chunk sections of some retrieval steps with one section.</summary>
    /// <param name="sourceStepIds">The IDs of the retrieval steps whose sections are replaced.</param>
    /// <param name="replacement">The new section. It goes to the place of the first replaced section.</param>
    public void ReplaceChunkSections(IReadOnlyCollection<string> sourceStepIds, PromptSection replacement)
    {
        ArgumentNullException.ThrowIfNull(sourceStepIds);
        ArgumentNullException.ThrowIfNull(replacement);

        bool IsReplaced(PromptSection section) =>
            section.Kind == PromptSectionKind.Chunks && section.SourceStepId is { } id && sourceStepIds.Contains(id);

        var index = _sections.FindIndex(IsReplaced);
        _sections.RemoveAll(IsReplaced);
        _sections.Insert(index < 0 ? _sections.Count : Math.Min(index, _sections.Count), replacement);
    }
}
