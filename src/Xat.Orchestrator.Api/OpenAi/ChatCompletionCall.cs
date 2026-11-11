using System.Text.Json;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.OpenAi;

/// <summary>A chat completion request after the facade mapped it to a pipeline run.</summary>
/// <param name="Model">The model name that the caller sent, in the form <c>pipeline/&lt;name&gt;</c>.</param>
/// <param name="Pipeline">The pipeline.</param>
/// <param name="Input">The input of the run.</param>
/// <param name="Stream">Shows whether the caller wants a stream.</param>
/// <param name="IncludeUsage">Shows whether the caller wants the token usage at the end of a stream.</param>
/// <param name="Debug">Shows whether the caller wants the step times in the <c>xat</c> object.</param>
/// <param name="Warnings">Notes about parts of the request that the facade did not use.</param>
internal sealed record ChatCompletionCall(
    string Model,
    PipelineDefinition Pipeline,
    JsonElement Input,
    bool Stream,
    bool IncludeUsage,
    bool Debug,
    IReadOnlyList<string> Warnings);
