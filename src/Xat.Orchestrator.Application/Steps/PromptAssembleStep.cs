using System.ComponentModel.DataAnnotations;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>
/// Builds the chat messages from all parts that the earlier steps added. The step escapes untrusted text,
/// and numbers the sources so that the model can cite them. The step does not limit the size of the prompt. The size depends on the number of chunks and on the engine.
/// </summary>
public sealed class PromptAssembleStep : IPipelineStep<PromptAssembleOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, PromptAssembleOptions options, CancellationToken ct)
    {
        var state = context.Run.State;
        var stepId = context.Step.Id;
        var draft = state.Prompt;

        if (string.IsNullOrWhiteSpace(draft.User))
        {
            return Done(StepOutcome.Fail(new PipelineError(
                "prompt.no_user", "The prompt has no text of the caller. Add a prompt.user step before this step.", ErrorKind.Validation, Source: stepId)));
        }

        var sections = Arrange(draft.Sections, options.Order);
        var chunkLists = new Dictionary<string, IReadOnlyList<Chunk>>(StringComparer.Ordinal);
        foreach (var section in sections.Where(section => section.Kind == PromptSectionKind.Chunks))
        {
            if (!state.TryGetOutput(section.SourceStepId!, out var output)
                || !output.TryGetValue("chunks", out var chunks)
                || chunks is not IReadOnlyList<Chunk> list)
            {
                return Done(StepOutcome.Fail(new PipelineError(
                    "prompt.chunks_missing", $"The step '{section.SourceStepId}' has no chunks.", ErrorKind.Validation, Source: stepId)));
            }

            chunkLists[section.Name] = list;
        }

        var hasUntrusted = sections.Any(section => section.Kind == PromptSectionKind.Chunks || !section.Trusted);
        var sources = new Dictionary<int, Chunk>();
        var renderedChunks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var number = 1;
        foreach (var section in sections.Where(section => section.Kind == PromptSectionKind.Chunks))
        {
            var rendered = new List<string>();
            foreach (var chunk in chunkLists[section.Name])
            {
                rendered.Add(PromptFormatting.RenderChunk(number, chunk));
                sources[number] = chunk;
                number++;
            }

            renderedChunks[section.Name] = rendered;
        }

        var parts = new List<string>();
        foreach (var section in sections)
        {
            if (section.Kind == PromptSectionKind.Text)
            {
                parts.Add(PromptFormatting.RenderText(section));
            }
            else if (renderedChunks[section.Name] is { Count: > 0 } rendered)
            {
                parts.Add($"<sources name=\"{PromptFormatting.EscapeAttribute(section.Name)}\">\n{string.Join("\n", rendered)}\n</sources>");
            }
        }

        parts.Add(draft.User);

        var messages = new List<ChatMessage>();
        var system = BuildSystem(draft.System, hasUntrusted, includeCitationNotice: sources.Count > 0);
        if (system.Length > 0)
        {
            messages.Add(new ChatMessage(ChatRole.System, system));
        }

        messages.Add(new ChatMessage(ChatRole.User, string.Join("\n\n", parts)));

        state.Set<IReadOnlyList<ChatMessage>>(StateKeys.Messages, messages);
        state.Set<IReadOnlyDictionary<int, Chunk>>(StateKeys.Sources, sources);
        return Done(StepOutcome.Continue);
    }

    private static ValueTask<StepOutcome> Done(StepOutcome outcome) => ValueTask.FromResult(outcome);

    private static List<PromptSection> Arrange(IReadOnlyList<PromptSection> sections, IReadOnlyList<string>? order)
    {
        if (order is null || order.Count == 0)
        {
            return [.. sections];
        }

        var arranged = new List<PromptSection>();
        foreach (var name in order)
        {
            arranged.AddRange(sections.Where(section => section.Name == name && !arranged.Any(known => ReferenceEquals(known, section))));
        }

        arranged.AddRange(sections.Where(section => !arranged.Any(known => ReferenceEquals(known, section))));
        return arranged;
    }

    private static string BuildSystem(string? system, bool hasUntrusted, bool includeCitationNotice)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(system))
        {
            lines.Add(system);
        }

        if (hasUntrusted)
        {
            lines.Add(PromptFormatting.UntrustedNotice);
        }

        if (hasUntrusted && includeCitationNotice)
        {
            lines.Add(PromptFormatting.CitationNotice);
        }

        return string.Join("\n\n", lines);
    }
}
