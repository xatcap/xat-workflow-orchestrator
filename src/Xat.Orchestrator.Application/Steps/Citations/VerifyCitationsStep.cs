using System.Text.RegularExpressions;
using Xat.Orchestrator.Application.Engine.Events;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;

namespace Xat.Orchestrator.Application.Steps.Citations;

/// <summary>
/// Checks the citations in the answer. The prompt numbers the sources, and the model cites the numbers, for example <c>[2]</c>.
/// The step maps each number to its chunk. It adds a note for a number that no source has and for an answer with no citation.
/// </summary>
public sealed partial class VerifyCitationsStep : IPipelineStep<VerifyCitationsOptions>
{
    [GeneratedRegex(@"\[(\d{1,3})\]")]
    private static partial Regex CitationPattern();

    /// <inheritdoc/>
    public async ValueTask<StepOutcome> ExecuteAsync(StepContext context, VerifyCitationsOptions options, CancellationToken ct)
    {
        var state = context.Run.State;
        if (!state.TryGet(StateKeys.Answer, out var answer))
        {
            return StepOutcome.Fail(new PipelineError(
                "citations.no_answer", "The run has no answer to check.", ErrorKind.Validation, Source: context.Step.Id));
        }

        if (!state.TryGet(StateKeys.Sources, out var sources) || sources.Count == 0)
        {
            return StepOutcome.Continue;
        }

        var citations = new List<Citation>();
        var unknown = new SortedSet<int>();
        foreach (Match match in CitationPattern().Matches(answer.Text))
        {
            var number = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (!sources.TryGetValue(number, out var chunk))
            {
                unknown.Add(number);
            }
            else if (citations.All(citation => citation.Number != number))
            {
                citations.Add(new Citation(number, chunk.ChunkId, chunk.Source.Title, SafeUri(chunk.Source.Uri)));
            }
        }

        if (unknown.Count > 0)
        {
            state.AddWarning($"The answer cites source number(s) {string.Join(", ", unknown)}, which the prompt did not hold.");
        }

        if (citations.Count == 0)
        {
            state.AddWarning("The answer has no citation of a source.");
        }

        state.Set(StateKeys.Answer, answer with { Citations = citations });
        await context.Run.Events.EmitAsync(new RunEvent.Citations(citations), ct);
        return StepOutcome.Continue;
    }

    /// <summary>
    /// The link comes from the document store, so it is untrusted. A caller can show it as a link, so the step keeps only
    /// links with the scheme http or https and links with no scheme. Any other scheme, for example <c>javascript:</c>, becomes empty.
    /// </summary>
    private static string SafeUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return string.Empty;
        }

        var hasScheme = uri.Contains(':', StringComparison.Ordinal) && Uri.TryCreate(uri, UriKind.Absolute, out _);
        if (!hasScheme)
        {
            return uri.Contains(':', StringComparison.Ordinal) ? string.Empty : uri;
        }

        var parsed = new Uri(uri);
        return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps ? uri : string.Empty;
    }
}
