using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.XatLlmEngine;

/// <summary>
/// The adapter to xat-llm-engine. The engine accepts the OpenAI chat format, but it refuses a top-level field that the provider
/// does not list. So the adapter writes the request by hand and sends only the fields that the model profile allows.
/// The adapter is a singleton. It gets a client from the factory for each call, so the factory can renew the handlers.
/// </summary>
internal sealed class XatEngineLlmClient(IHttpClientFactory factory) : ILlmClient
{
    private const string Name = "xat-llm-engine";

    /// <inheritdoc/>
    public async Task<Result<Completion>> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var http = factory.CreateClient(DependencyInjection.XatLlmEngineClient);
        try
        {
            using var content = BuildRequest(request, stream: false);
            using var response = await http.PostAsync("/v1/chat/completions", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<Completion>.Fail(await UpstreamErrors.FromResponseAsync(response, Name, ct));
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var choice = root.GetProperty("choices")[0];
            var answer = choice.GetProperty("message").TryGetProperty("content", out var contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;
            var finish = choice.TryGetProperty("finish_reason", out var finishElement) ? finishElement.GetString() : null;
            return Result<Completion>.Ok(new Completion(answer, finish, ReadUsage(root)));
        }
        catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error))
        {
            return Result<Completion>.Fail(error);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return Result<Completion>.Fail(InvalidResponse());
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Result<CompletionDelta>> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var http = factory.CreateClient(DependencyInjection.XatLlmEngineClient);
        HttpResponseMessage? response = null;
        PipelineError? failure = null;
        try
        {
            using var content = BuildRequest(request, stream: true);
            using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = content };
            response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                failure = await UpstreamErrors.FromResponseAsync(response, Name, ct);
            }
        }
        catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error))
        {
            failure = error;
        }
        catch
        {
            // An exception that is not an upstream failure goes on, but the connection must not stay open.
            response?.Dispose();
            throw;
        }

        if (failure is not null)
        {
            response?.Dispose();
            yield return Result<CompletionDelta>.Fail(failure);
            yield break;
        }

        using (response)
        {
            Stream? stream = null;
            try
            {
                stream = await response!.Content.ReadAsStreamAsync(ct);
            }
            catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error, UpstreamCallPhase.StreamBody))
            {
                failure = error;
            }

            if (failure is not null)
            {
                yield return Result<CompletionDelta>.Fail(failure);
                yield break;
            }

            await using var body = stream!;
            using var reader = new StreamReader(body, Encoding.UTF8);
            var finished = false;
            var sawFinishReason = false;

            while (true)
            {
                var (line, readError) = await ReadLineAsync(reader, ct);
                if (readError is not null)
                {
                    yield return Result<CompletionDelta>.Fail(readError);
                    yield break;
                }

                if (line is null)
                {
                    break;
                }

                // A comment line, for example ": keep-alive", and an empty line between events carry no data.
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line[5..].TrimStart();
                if (payload == "[DONE]")
                {
                    finished = true;
                    break;
                }

                var parsed = ParseChunk(payload);
                if (!parsed.IsSuccess)
                {
                    yield return Result<CompletionDelta>.Fail(parsed.Error);
                    yield break;
                }

                sawFinishReason |= parsed.Value.FinishReason is not null;
                yield return parsed;
            }

            // The engine ends a failed stream with an error event and no [DONE]. A stream that ends with neither is cut off.
            if (!finished && !sawFinishReason)
            {
                yield return Result<CompletionDelta>.Fail(UpstreamErrors.StreamIncomplete(Name));
            }
        }
    }

    private static async Task<(string? Line, PipelineError? Error)> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            return (await reader.ReadLineAsync(ct), null);
        }
        catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error, UpstreamCallPhase.StreamBody))
        {
            return (null, error);
        }
    }

    /// <summary>Builds the request body. The method adds a field only when the model profile lists it.</summary>
    /// <param name="request">The request.</param>
    /// <param name="stream">Shows whether the request asks for a stream.</param>
    /// <returns>The body as JSON content.</returns>
    internal static StringContent BuildRequest(LlmRequest request, bool stream)
    {
        var allowed = new HashSet<string>(request.Model.AllowedFields, StringComparer.Ordinal);

        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            messages.Add(new JsonObject { ["role"] = RoleName(message.Role), ["content"] = message.Content });
        }

        var body = new JsonObject
        {
            ["model"] = request.Model.Model,
            ["messages"] = messages,
            ["stream"] = stream,
        };

        if (request.Temperature is { } temperature && allowed.Contains("temperature"))
        {
            body["temperature"] = temperature;
        }

        var tokenField = allowed.Contains("max_tokens") ? "max_tokens"
            : allowed.Contains("max_completion_tokens") ? "max_completion_tokens"
            : null;
        if (request.MaxTokens is { } maxTokens && tokenField is not null)
        {
            body[tokenField] = maxTokens;
        }

        if (stream && allowed.Contains("stream_options"))
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        return new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
    }

    private static Result<CompletionDelta> ParseChunk(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            // The engine reports a failure after the first piece as an event with the body {"error":{...}}.
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<CompletionDelta>.Fail(InvalidResponse());
            }

            if (root.TryGetProperty("error", out var error))
            {
                var type = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;
                var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : null;
                return Result<CompletionDelta>.Fail(
                    UpstreamErrors.Map(System.Net.HttpStatusCode.BadGateway, type, message, null, Name));
            }

            var text = string.Empty;
            string? finish = null;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.String)
                {
                    text = contentElement.GetString() ?? string.Empty;
                }

                if (choice.TryGetProperty("finish_reason", out var finishElement) && finishElement.ValueKind == JsonValueKind.String)
                {
                    finish = finishElement.GetString();
                }
            }

            return Result<CompletionDelta>.Ok(new CompletionDelta(text, finish, ReadUsage(root)));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Result<CompletionDelta>.Fail(InvalidResponse());
        }
    }

    private static TokenUsage? ReadUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            var prompt = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pValue) ? pValue : 0;
            var completion = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cValue) ? cValue : 0;
            return new TokenUsage(prompt, completion);
        }

        return null;
    }

    private static string RoleName(ChatRole role) =>
        role switch
        {
            ChatRole.System => "system",
            ChatRole.Assistant => "assistant",
            _ => "user",
        };

    private static PipelineError InvalidResponse() =>
        new($"{Name}.invalid_response", $"{Name} sent an answer that the orchestrator cannot read.", ErrorKind.UpstreamFailed, Source: Name);
}
