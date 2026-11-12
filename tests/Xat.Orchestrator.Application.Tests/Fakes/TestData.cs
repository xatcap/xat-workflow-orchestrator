using System.Text.Json;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>Builds test data.</summary>
internal static class TestData
{
    public static Chunk Chunk(string id, string title, string text, string heading = "Section") =>
        new(id, null, "section", 1.0, new UntrustedText(heading), new UntrustedText(text), new ChunkSource("docs", id, "markdown", $"https://example.test/{id}", title, "1"));

    public static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static StepDefinition Step(string id, string uses, string withJson = "{}") => new(id, uses, Json(withJson));

    public static PipelineDefinition Pipeline(
        string name,
        IReadOnlyList<StepDefinition> steps,
        string schemaJson = "{}",
        StreamMode stream = StreamMode.Allowed) =>
        new(name, 1, "Test pipeline.", stream, Json(schemaJson), steps);

    /// <summary>The standard question pipeline: system, user, retrieve, no-answer check, assemble, generate, citations.</summary>
    public static PipelineDefinition RagQa(string profile = "default") =>
        Pipeline(
            "rag-qa",
            [
                Step("system", "prompt.system", """{"text":"${input.system_prompt ?? 'Answer from the sources.'}"}"""),
                Step("user", "prompt.user", """{"text":"${input.prompt}"}"""),
                Step("retrieve", "rag.retrieve", $$"""{"profile":"{{profile}}"}"""),
                Step("none", "flow.complete-if", """{"key":"steps.retrieve.chunks","isEmpty":true,"text":"No sources."}"""),
                Step("assemble", "prompt.assemble"),
                Step("generate", "llm.generate"),
                Step("cite", "answer.verify-citations"),
            ],
            """{"type":"object","required":["prompt"],"properties":{"prompt":{"type":"string","minLength":1}}}""");
}
