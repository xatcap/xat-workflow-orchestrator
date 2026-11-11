using System.Text.Json.Nodes;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.OpenAi;

/// <summary>Builds the JSON bodies of the OpenAI facade. The field names are the names of the OpenAI chat completion format.</summary>
internal static class ChatCompletionJson
{
    /// <summary>Makes the ID of a completion from the ID of the run.</summary>
    public static string Id(string runId) => "chatcmpl-" + runId;

    /// <summary>Makes the complete answer of a run that does not stream.</summary>
    public static JsonObject Completion(string model, long created, RunResult result, IReadOnlyList<string> warnings, bool debug) =>
        new()
        {
            ["id"] = Id(result.RunId),
            ["object"] = "chat.completion",
            ["created"] = created,
            ["model"] = model,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = result.Answer.Text },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = Usage(result),
            ["xat"] = Extension(result, warnings, debug),
        };

    /// <summary>Makes one piece of a stream. A <see langword="null"/> value for the content, the role or the finish reason writes a JSON null or leaves the field out, as OpenAI does.</summary>
    public static JsonObject Chunk(string id, string model, long created, string? role, string? content, string? finishReason, JsonObject? extension = null)
    {
        var delta = new JsonObject();
        if (role is not null)
        {
            delta["role"] = role;
        }

        if (content is not null)
        {
            delta["content"] = content;
        }

        var chunk = new JsonObject
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["model"] = model,
            ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finishReason }),
        };

        if (extension is not null)
        {
            chunk["xat"] = extension;
        }

        return chunk;
    }

    /// <summary>Makes the last piece of a stream with the token usage. The list of choices is empty, as in the OpenAI format.</summary>
    public static JsonObject UsageChunk(string id, string model, long created, RunResult result) =>
        new()
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["model"] = model,
            ["choices"] = new JsonArray(),
            ["usage"] = Usage(result),
        };

    /// <summary>Makes the <c>xat</c> object. It carries what the OpenAI format has no place for: the run ID, the status, the citations and the notes.</summary>
    public static JsonObject Extension(RunResult result, IReadOnlyList<string> mappingWarnings, bool debug)
    {
        var citations = new JsonArray();
        foreach (var citation in result.Answer.Citations)
        {
            citations.Add(new JsonObject
            {
                ["number"] = citation.Number,
                ["chunk_id"] = citation.ChunkId,
                ["title"] = citation.Title,
                ["uri"] = citation.Uri,
            });
        }

        var warnings = new JsonArray();
        foreach (var warning in mappingWarnings.Concat(result.Warnings))
        {
            warnings.Add(warning);
        }

        var extension = new JsonObject
        {
            ["run_id"] = result.RunId,
            ["pipeline"] = result.Pipeline,
            ["status"] = result.Status == RunStatus.NoAnswer ? "no_answer" : "answered",
            ["citations"] = citations,
            ["warnings"] = warnings,
        };

        if (debug)
        {
            var steps = new JsonArray();
            foreach (var step in result.Steps)
            {
                steps.Add(new JsonObject { ["id"] = step.StepId, ["uses"] = step.Uses, ["duration_ms"] = Math.Round(step.Duration.TotalMilliseconds, 1) });
            }

            extension["debug"] = new JsonObject { ["steps"] = steps };
        }

        return extension;
    }

    /// <summary>Makes an error body in the OpenAI form: <c>{"error":{"message","type","param","code"}}</c>.</summary>
    public static JsonObject Error(PipelineError error) =>
        new()
        {
            ["error"] = new JsonObject
            {
                ["message"] = error.Message,
                ["type"] = ApiErrors.Describe(error.Kind).Type,
                ["param"] = null,
                ["code"] = error.Code,
            },
        };

    private static JsonObject Usage(RunResult result) =>
        new()
        {
            ["prompt_tokens"] = result.Usage.PromptTokens,
            ["completion_tokens"] = result.Usage.CompletionTokens,
            ["total_tokens"] = result.Usage.PromptTokens + result.Usage.CompletionTokens,
        };
}
