using System.Text.Json;
using System.Text.Json.Nodes;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.OpenAi;

/// <summary>
/// Reads an OpenAI chat completion request and maps it to the input of a pipeline.
/// The model <c>pipeline/&lt;name&gt;</c> selects the pipeline. The last user message is the <c>prompt</c> of the pipeline.
/// The system messages are the <c>system_prompt</c>, when the pipeline declares that input. The facade uses no earlier turns,
/// because the pipelines take one prompt. The facade does not hide this: it adds a note to the answer.
/// </summary>
internal static class ChatCompletionRequestReader
{
    /// <summary>The prefix of a model name. The name after the prefix is the name of the pipeline.</summary>
    public const string ModelPrefix = "pipeline/";

    private static readonly string[] _ignoredFields =
    [
        "temperature", "max_tokens", "max_completion_tokens", "top_p", "frequency_penalty", "presence_penalty", "seed",
        "stop", "user", "logit_bias", "logprobs", "top_logprobs", "store", "metadata", "service_tier", "reasoning_effort",
    ];

    private static readonly string[] _rejectedFields =
        ["tools", "tool_choice", "functions", "function_call", "audio", "modalities", "prediction", "parallel_tool_calls"];

    /// <summary>Reads the request.</summary>
    /// <param name="body">The JSON body.</param>
    /// <param name="catalog">The pipelines.</param>
    /// <returns>The call, or a validation error. The second item of a failed result is not used. The error message names the field and never repeats a value.</returns>
    public static Result<ChatCompletionCall> Read(JsonElement body, IPipelineCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (body.ValueKind != JsonValueKind.Object)
        {
            return Fail("request.invalid_json", "The request body must be a JSON object.");
        }

        foreach (var field in _rejectedFields)
        {
            if (body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                return Fail("request.unsupported_field", $"The field '{field}' is not supported. Pipelines do not call tools.");
            }
        }

        if (body.TryGetProperty("n", out var n) && n.ValueKind != JsonValueKind.Null && !(n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var count) && count == 1))
        {
            return Fail("request.unsupported_field", "The field 'n' must be 1.");
        }

        if (body.TryGetProperty("response_format", out var format)
            && format.ValueKind == JsonValueKind.Object
            && format.TryGetProperty("type", out var formatType)
            && formatType.ValueKind == JsonValueKind.String
            && formatType.GetString() != "text")
        {
            return Fail("request.unsupported_field", "The field 'response_format' must have the type text. Structured output is not built yet.");
        }

        if (HasWrongType(body, "stream", JsonValueKind.True, JsonValueKind.False) || HasWrongType(body, "stream_options", JsonValueKind.Object))
        {
            return Fail("request.invalid_field", "The field 'stream' must be true or false, and 'stream_options' must be an object.");
        }

        var pipeline = ReadPipeline(body, catalog);
        if (!pipeline.IsSuccess)
        {
            return Result<ChatCompletionCall>.Fail(pipeline.Error);
        }

        var messages = ReadMessages(body);
        if (!messages.IsSuccess)
        {
            return Result<ChatCompletionCall>.Fail(messages.Error);
        }

        return BuildCall(body, pipeline.Value, messages.Value);
    }

    private static Result<ChatCompletionCall> BuildCall(JsonElement body, PipelineDefinition pipeline, IReadOnlyList<Message> messages)
    {
        var declared = DeclaredInputs(pipeline);
        if (!declared.Contains("prompt"))
        {
            return Fail("request.pipeline_not_chat", "This pipeline has no input named prompt, so a chat request cannot call it. Call it with POST /v1/pipelines/{name}/runs.");
        }

        var lastUser = messages.ToList().FindLastIndex(message => message.Role == "user");
        if (lastUser < 0)
        {
            return Fail("request.no_user_message", "The messages need at least one message with the role user.");
        }

        var warnings = new List<string>();
        var input = new JsonObject();

        // The extension field xat.input gives the other inputs of the pipeline, for example the context.
        // The messages win for the prompt. They also win for system_prompt when the request has a system message.
        var (extraInput, debug) = ReadExtension(body);
        foreach (var (name, value) in extraInput)
        {
            input[name] = value;
        }

        input["prompt"] = messages[lastUser].Text;

        var system = string.Join("\n\n", messages.Where(message => message.Role is "system" or "developer").Select(message => message.Text));
        if (system.Length > 0)
        {
            if (declared.Contains("system_prompt"))
            {
                input["system_prompt"] = system;
            }
            else
            {
                warnings.Add("The system messages were not used, because the pipeline has no input named system_prompt.");
            }
        }

        var unused = messages.Count(message => message.Role is "user" or "assistant") - 1;
        if (unused > 0)
        {
            warnings.Add($"{unused} earlier message(s) were not used. The pipeline answers the last user message only.");
        }

        var ignored = _ignoredFields.Where(field => body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null).ToList();
        if (ignored.Count > 0)
        {
            warnings.Add($"The fields {string.Join(", ", ignored)} were not used. The pipeline sets them.");
        }

        var stream = body.TryGetProperty("stream", out var streamValue) && streamValue.ValueKind == JsonValueKind.True;
        var includeUsage = body.TryGetProperty("stream_options", out var options)
            && options.ValueKind == JsonValueKind.Object
            && options.TryGetProperty("include_usage", out var usage)
            && usage.ValueKind == JsonValueKind.True;

        return Result<ChatCompletionCall>.Ok(new ChatCompletionCall(
            ModelPrefix + pipeline.Name,
            pipeline,
            JsonSerializer.SerializeToElement(input),
            stream,
            includeUsage,
            debug,
            warnings));
    }

    /// <summary>Shows whether a field is present, is not null, and has a type other than the allowed types.</summary>
    private static bool HasWrongType(JsonElement body, string field, params JsonValueKind[] allowed) =>
        body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null && !allowed.Contains(value.ValueKind);

    /// <summary>Reads the request extension <c>xat</c>: the extra inputs of the pipeline and the debug option.</summary>
    private static (List<KeyValuePair<string, JsonNode?>> Input, bool Debug) ReadExtension(JsonElement body)
    {
        var input = new List<KeyValuePair<string, JsonNode?>>();
        if (!body.TryGetProperty("xat", out var xat) || xat.ValueKind != JsonValueKind.Object)
        {
            return (input, false);
        }

        if (xat.TryGetProperty("input", out var extra) && extra.ValueKind == JsonValueKind.Object)
        {
            input.AddRange(extra.EnumerateObject().Select(property => KeyValuePair.Create(property.Name, JsonNode.Parse(property.Value.GetRawText()))));
        }

        var debug = xat.TryGetProperty("debug", out var debugValue) && debugValue.ValueKind == JsonValueKind.True;
        return (input, debug);
    }

    private static Result<PipelineDefinition> ReadPipeline(JsonElement body, IPipelineCatalog catalog)
    {
        if (!body.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String)
        {
            return Result<PipelineDefinition>.Fail(new PipelineError(
                "request.model_missing", "The field 'model' is required. Use pipeline/<name>. GET /v1/models lists the names.", ErrorKind.Validation));
        }

        var text = model.GetString() ?? string.Empty;
        if (!text.StartsWith(ModelPrefix, StringComparison.Ordinal))
        {
            return Result<PipelineDefinition>.Fail(new PipelineError(
                "request.model_invalid", "The model must have the form pipeline/<name>. GET /v1/models lists the names.", ErrorKind.Validation));
        }

        var pipeline = catalog.Find(text[ModelPrefix.Length..]);
        return pipeline.IsSuccess
            ? pipeline
            : Result<PipelineDefinition>.Fail(new PipelineError(
                "model.not_found", "The model does not exist. GET /v1/models lists the names.", ErrorKind.NotFound));
    }

    private static Result<IReadOnlyList<Message>> ReadMessages(JsonElement body)
    {
        if (!body.TryGetProperty("messages", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            return Result<IReadOnlyList<Message>>.Fail(new PipelineError(
                "request.messages_missing", "The field 'messages' must be a list with at least one message.", ErrorKind.Validation));
        }

        var messages = new List<Message>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("role", out var roleElement)
                || roleElement.ValueKind != JsonValueKind.String)
            {
                return Result<IReadOnlyList<Message>>.Fail(new PipelineError(
                    "request.message_invalid", "Each message must be an object with a role.", ErrorKind.Validation));
            }

            var role = roleElement.GetString()!;
            if (role is not ("system" or "developer" or "user" or "assistant"))
            {
                return Result<IReadOnlyList<Message>>.Fail(new PipelineError(
                    "request.unsupported_role", "The role of a message must be system, developer, user or assistant.", ErrorKind.Validation));
            }

            var text = ReadText(item);
            if (!text.IsSuccess)
            {
                return Result<IReadOnlyList<Message>>.Fail(text.Error);
            }

            messages.Add(new Message(role, text.Value));
        }

        return Result<IReadOnlyList<Message>>.Ok(messages);
    }

    /// <summary>Reads the content of a message. The content is a text, or a list of parts of the type text.</summary>
    private static Result<string> ReadText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
        {
            return Result<string>.Ok(string.Empty);
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return Result<string>.Ok(content.GetString() ?? string.Empty);
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return Fail<string>("request.message_invalid", "The content of a message must be a text or a list of text parts.");
        }

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "text"
                || !part.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                return Fail<string>("request.unsupported_content", "Only content parts of the type text are supported.");
            }

            parts.Add(text.GetString() ?? string.Empty);
        }

        return Result<string>.Ok(string.Join("\n", parts));
    }

    /// <summary>
    /// Shows whether a plain chat request can call the pipeline. The pipeline must declare an input named <c>prompt</c>,
    /// must not need other inputs, and must not always stream, because an OpenAI client does not stream by default.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns><see langword="true"/> when the pipeline can be called with a chat request.</returns>
    public static bool IsCallable(PipelineDefinition pipeline) =>
        pipeline.Stream != StreamMode.Required
        && DeclaredInputs(pipeline).Contains("prompt")
        && RequiredInputs(pipeline).All(name => name is "prompt" or "system_prompt");

    /// <summary>Lists the input names that the input schema of the pipeline marks as required.</summary>
    private static IEnumerable<string> RequiredInputs(PipelineDefinition pipeline) =>
        pipeline.InputSchema.ValueKind == JsonValueKind.Object
        && pipeline.InputSchema.TryGetProperty("required", out var required)
        && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray().Select(item => item.GetString() ?? string.Empty)
            : [];

    /// <summary>Lists the input names that the pipeline declares in its input schema.</summary>
    private static HashSet<string> DeclaredInputs(PipelineDefinition pipeline)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (pipeline.InputSchema.ValueKind == JsonValueKind.Object
            && pipeline.InputSchema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
            }
        }

        return names;
    }

    private static Result<ChatCompletionCall> Fail(string code, string message) => Fail<ChatCompletionCall>(code, message);

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Fail(new PipelineError(code, message, ErrorKind.Validation));

    private sealed record Message(string Role, string Text);
}
