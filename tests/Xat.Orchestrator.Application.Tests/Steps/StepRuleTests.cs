using Microsoft.Extensions.DependencyInjection;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Runs;
using Xat.Orchestrator.Application.Tests.Fakes;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Tests.Steps;

/// <summary>Tests rules of the built-in steps that protect the caller: the safe source links and the early end of a run.</summary>
public class StepRuleTests
{
    private static Chunk ChunkWithUri(string uri) =>
        new("c1", null, "section", 1.0, new UntrustedText("Section"), new UntrustedText("The port is 8080."), new ChunkSource("docs", "c1", "markdown", uri, "Orders", "1"));

    /// <summary>
    /// The link of a citation comes from a document, so an attacker can write it. Only http, https and a relative path stay.
    /// A link such as <c>javascript:</c> or <c>data:</c> becomes empty, because a client could run it.
    /// </summary>
    [Theory]
    [InlineData("https://example.test/a", "https://example.test/a")]
    [InlineData("http://example.test/a", "http://example.test/a")]
    [InlineData("docs/orders.md", "docs/orders.md")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("JAVASCRIPT:alert(1)", "")]
    [InlineData("data:text/html,<script>alert(1)</script>", "")]
    [InlineData("file:///etc/passwd", "")]
    [InlineData("vbscript:msgbox", "")]
    public async Task CitationLink_KeepsOnlySafeSchemes(string uri, string expected)
    {
        var rag = new FakeRetrievalClient { OnRetrieve = _ => Result<RetrievedSet>.Ok(new RetrievedSet([ChunkWithUri(uri)], 0)) };
        using var services = TestHost.Build(rag);

        var result = await services.GetRequiredService<PipelineRunner>()
            .Start(TestData.RagQa(), TestData.Json("""{"prompt":"Which port?"}"""), new RunRequest(false), TestContext.Current.CancellationToken)
            .Completion;

        Assert.Equal(expected, Assert.Single(result.Value!.Answer.Citations).Uri);
    }

    /// <summary>With <c>equals</c>, the step ends the run when the value as text is equal. Otherwise the run goes on.</summary>
    [Theory]
    [InlineData("skip", RunStatus.NoAnswer)]
    [InlineData("go", RunStatus.Answered)]
    public async Task CompleteIf_Equals_EndsTheRunOnlyOnAMatch(string mode, RunStatus expected)
    {
        using var services = TestHost.Build();
        var pipeline = TestData.Pipeline(
            "t",
            [
                TestData.Step("user", "prompt.user", """{"text":"${input.prompt}"}"""),
                TestData.Step("stop", "flow.complete-if", """{"key":"input.mode","equals":"skip","text":"Skipped."}"""),
                TestData.Step("assemble", "prompt.assemble"),
                TestData.Step("generate", "llm.generate"),
            ]);

        var result = await services.GetRequiredService<PipelineRunner>()
            .Start(pipeline, TestData.Json($$"""{"prompt":"Q","mode":"{{mode}}"}"""), new RunRequest(false), TestContext.Current.CancellationToken)
            .Completion;

        Assert.Equal(expected, result.Value!.Status);
        Assert.Equal(expected == RunStatus.NoAnswer, result.Value!.Answer.Text == "Skipped.");
    }
}
