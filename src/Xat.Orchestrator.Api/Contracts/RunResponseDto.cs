using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The result of a run.</summary>
/// <param name="RunId">The ID of the run.</param>
/// <param name="Pipeline">The name of the pipeline.</param>
/// <param name="Version">The version of the pipeline.</param>
/// <param name="Status">Either <c>answered</c> or <c>no_answer</c>.</param>
/// <param name="Answer">The answer.</param>
/// <param name="Usage">The token usage of the LLM calls.</param>
/// <param name="Warnings">Notes about the run. A note is not an error.</param>
/// <param name="Debug">The step times, when the request asked for them.</param>
internal sealed record RunResponseDto(
    string RunId,
    string Pipeline,
    int Version,
    string Status,
    AnswerDto Answer,
    UsageDto Usage,
    IReadOnlyList<string> Warnings,
    DebugDto? Debug)
{
    /// <summary>Makes the response from a run result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="includeDebug">Shows whether to add the step times.</param>
    /// <returns>The response.</returns>
    public static RunResponseDto From(RunResult result, bool includeDebug) =>
        new(
            result.RunId,
            result.Pipeline,
            result.Version,
            result.Status == RunStatus.NoAnswer ? "no_answer" : "answered",
            new AnswerDto(result.Answer.Text, result.Answer.Citations.Select(CitationDto.From).ToList()),
            new UsageDto(result.Usage.PromptTokens, result.Usage.CompletionTokens),
            result.Warnings,
            includeDebug
                ? new DebugDto(result.Steps.Select(step => new StepTimingDto(step.StepId, step.Uses, Math.Round(step.Duration.TotalMilliseconds, 1))).ToList())
                : null);
}
