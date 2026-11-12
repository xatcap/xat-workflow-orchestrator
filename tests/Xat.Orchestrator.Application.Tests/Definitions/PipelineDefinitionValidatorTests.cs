using Microsoft.Extensions.DependencyInjection;
using Xat.Orchestrator.Application.Definitions;
using Xat.Orchestrator.Application.Tests.Fakes;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Application.Tests.Definitions;

/// <summary>Tests the check of pipeline definitions at start. A mistake must show at start, not in a request.</summary>
public class PipelineDefinitionValidatorTests
{
    private static IReadOnlyList<string> Validate(PipelineDefinition pipeline)
    {
        using var services = TestHost.Build();
        return services.GetRequiredService<PipelineDefinitionValidator>().Validate(pipeline);
    }

    /// <summary>The standard pipeline is valid.</summary>
    [Fact]
    public void ValidPipeline_HasNoProblem()
    {
        Assert.Empty(Validate(TestData.RagQa()));
    }

    /// <summary>Each mistake gives a message that names the step.</summary>
    [Theory]
    [InlineData("""[{"id":"u","uses":"nope.step"},{"id":"g","uses":"llm.generate"}]""", "unknown step type 'nope.step'")]
    [InlineData("""[{"id":"u","uses":"prompt.user","with":"{\"text\":\"a\"}"},{"id":"u","uses":"llm.generate"}]""", "used more than once")]
    [InlineData("""[{"id":"r","uses":"rag.retrieve","with":"{\"profile\":\"missing\"}"},{"id":"g","uses":"llm.generate"}]""", "not configured")]
    [InlineData("""[{"id":"r","uses":"rag.retrieve","with":"{}"},{"id":"g","uses":"llm.generate"}]""", "Profile field is required")]
    [InlineData("""[{"id":"r","uses":"rag.retrieve","with":"{\"profile\":\"default\",\"typo\":1}"},{"id":"g","uses":"llm.generate"}]""", "typo")]
    [InlineData("""[{"id":"g","uses":"llm.generate","with":"{\"model\":\"missing\"}"}]""", "not configured")]
    [InlineData("""[{"id":"s","uses":"prompt.system","with":"{\"template\":\"missing@1\"}"},{"id":"g","uses":"llm.generate"}]""", "does not exist")]
    [InlineData("""[{"id":"s","uses":"prompt.system","with":"{\"text\":\"a\",\"template\":\"rules@1\"}"},{"id":"g","uses":"llm.generate"}]""", "exactly one of 'text' and 'template'")]
    [InlineData("""[{"id":"s","uses":"prompt.user","with":"{\"text\":\"${steps.later.out}\"}"},{"id":"later","uses":"rag.retrieve","with":"{\"profile\":\"default\"}"},{"id":"g","uses":"llm.generate"}]""", "name an earlier step")]
    [InlineData("""[{"id":"s","uses":"prompt.user","with":"{\"text\":\"${other.x}\"}"},{"id":"g","uses":"llm.generate"}]""", "must start with 'input.' or 'steps.'")]
    [InlineData("""[{"id":"m","uses":"retrieval.merge","with":"{\"from\":[\"later\"]}"},{"id":"later","uses":"rag.retrieve","with":"{\"profile\":\"default\"}"},{"id":"g","uses":"llm.generate"}]""", "not an earlier step")]
    [InlineData("""[{"id":"c","uses":"flow.complete-if","with":"{\"key\":\"input.x\",\"text\":\"t\"}"},{"id":"g","uses":"llm.generate"}]""", "exactly one of 'isEmpty' and 'equals'")]
    [InlineData("""[{"id":"u","uses":"prompt.user","with":"{\"text\":\"a\"}"}]""", "no step produces an answer")]
    public void Mistake_GivesAMessage(string stepsJson, string expectedPart)
    {
        var steps = System.Text.Json.JsonSerializer.Deserialize<List<Spec>>(stepsJson, TestData.WebOptions)!
            .Select(spec => TestData.Step(spec.Id, spec.Uses, spec.With ?? "{}"))
            .ToList();

        var problems = Validate(TestData.Pipeline("t", steps));

        Assert.Contains(problems, problem => problem.Contains(expectedPart, StringComparison.Ordinal));
    }

    /// <summary>
    /// An option that is a number or a flag can hold one expression. Its value is known only when the run starts, so the start accepts it.
    /// </summary>
    [Theory]
    [InlineData("""[{"id":"r","uses":"rag.retrieve","with":"{\"profile\":\"default\",\"chunks\":\"${input.n}\"}"},{"id":"g","uses":"llm.generate","with":"{\"temperature\":\"${input.t ?? '0.2'}\"}"}]""")]
    [InlineData("""[{"id":"r","uses":"rag.retrieve","with":"{\"profile\":\"default\"}"},{"id":"c","uses":"flow.complete-if","with":"{\"key\":\"steps.r.chunks\",\"isEmpty\":\"${input.flag}\",\"text\":\"t\"}"},{"id":"g","uses":"llm.generate"}]""")]
    public void TypedOptionWithOneExpression_IsAccepted(string stepsJson)
    {
        Assert.Empty(Validate(TestData.Pipeline("t", Steps(stepsJson))));
    }

    /// <summary>A number option that mixes an expression with other text can never be a number, so the start refuses it.</summary>
    [Fact]
    public void TypedOptionWithMixedText_IsRefused()
    {
        var steps = Steps("""[{"id":"r","uses":"rag.retrieve","with":"{\"profile\":\"default\",\"chunks\":\"n${input.n}\"}"},{"id":"g","uses":"llm.generate"}]""");

        Assert.NotEmpty(Validate(TestData.Pipeline("t", steps)));
    }

    private static List<Domain.Pipelines.StepDefinition> Steps(string stepsJson) =>
        System.Text.Json.JsonSerializer.Deserialize<List<Spec>>(stepsJson, TestData.WebOptions)!
            .Select(spec => TestData.Step(spec.Id, spec.Uses, spec.With ?? "{}"))
            .ToList();

    /// <summary>A pipeline with no steps, a bad name or a bad version is not valid.</summary>
    [Fact]
    public void BadNameVersionAndNoSteps_AreFound()
    {
        var pipeline = new PipelineDefinition("Bad Name", 0, string.Empty, StreamMode.Allowed, default, []);

        var problems = Validate(pipeline);

        Assert.Contains(problems, problem => problem.Contains("the name must match", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("version must be 1", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("no steps", StringComparison.Ordinal));
    }

    /// <summary>A pipeline with the stream mode required needs a step that can stream.</summary>
    [Fact]
    public void StreamRequired_WithoutAStreamingStep_IsNotValid()
    {
        var pipeline = TestData.Pipeline(
            "t",
            [TestData.Step("c", "flow.complete-if", """{"key":"input.x","isEmpty":true,"text":"t"}""")],
            stream: StreamMode.Required);

        Assert.Contains(Validate(pipeline), problem => problem.Contains("no step can stream", StringComparison.Ordinal));
    }

    /// <summary>An input schema with a keyword that the validator cannot enforce is not valid.</summary>
    [Fact]
    public void UnsupportedSchemaKeyword_IsNotValid()
    {
        var pipeline = TestData.Pipeline(
            "t",
            [TestData.Step("g", "llm.generate")],
            schemaJson: """{"type":"object","properties":{"a":{"$ref":"#/x"}}}""");

        Assert.Contains(Validate(pipeline), problem => problem.Contains("'$ref'", StringComparison.Ordinal));
    }

    private sealed record Spec(string Id, string Uses, string? With);
}
