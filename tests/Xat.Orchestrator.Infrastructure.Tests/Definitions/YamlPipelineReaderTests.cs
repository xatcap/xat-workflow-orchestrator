using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.Tests.Definitions;

/// <summary>Tests the reader of pipeline files.</summary>
public class YamlPipelineReaderTests
{
    private const string Valid = """
        name: demo
        version: 2
        summary: A demo.
        stream: never
        input:
          type: object
          required: [prompt]
          properties:
            prompt: { type: string, maxLength: 10 }
        steps:
          - uses: prompt.user
            with:
              text: "${input.prompt}"
          - id: fetch
            uses: rag.retrieve
            with:
              profile: docs
              chunks: 4
              flag: true
              ratio: 0.5
              none: ~
              quoted: "7"
          - uses: rag.retrieve
          - uses: llm.generate
        """;

    /// <summary>A valid file gives the pipeline with typed options. A quoted number stays text.</summary>
    [Fact]
    public void ValidFile_IsRead_WithTypedValues()
    {
        var result = YamlPipelineReader.Read(Valid, "demo.yaml");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var pipeline = result.Value!;
        Assert.Equal(("demo", 2, "A demo.", StreamMode.Never), (pipeline.Name, pipeline.Version, pipeline.Summary, pipeline.Stream));
        var with = pipeline.Steps[1].With;
        Assert.Equal(4, with.GetProperty("chunks").GetInt32());
        Assert.True(with.GetProperty("flag").GetBoolean());
        Assert.Equal(0.5, with.GetProperty("ratio").GetDouble());
        Assert.Equal(JsonValueKind.Null, with.GetProperty("none").ValueKind);
        Assert.Equal(JsonValueKind.String, with.GetProperty("quoted").ValueKind);
        Assert.Equal("${input.prompt}", pipeline.Steps[0].With.GetProperty("text").GetString());
        Assert.Equal("object", pipeline.InputSchema.GetProperty("type").GetString());
    }

    /// <summary>A step with no id gets the step type as its id. A repeat gets a number.</summary>
    [Fact]
    public void StepsWithoutId_GetStableIds()
    {
        var result = YamlPipelineReader.Read(Valid, "demo.yaml");

        Assert.Equal(["prompt-user", "fetch", "rag-retrieve", "llm-generate"], result.Value!.Steps.Select(step => step.Id));
        var repeated = YamlPipelineReader.Read("name: x\nsteps:\n  - uses: rag.retrieve\n  - uses: rag.retrieve\n  - uses: rag.retrieve\n", "x.yaml");
        Assert.Equal(["rag-retrieve", "rag-retrieve-2", "rag-retrieve-3"], repeated.Value!.Steps.Select(step => step.Id));
    }

    /// <summary>The reader reports a spelling mistake in a key.</summary>
    [Theory]
    [InlineData("name: x\nsteps: []\nsteeps: 1\n", "the key 'steeps' is not known")]
    [InlineData("name: x\nsteps:\n  - uses: a\n    wiht: {}\n", "has the key 'wiht'")]
    [InlineData("steps: []\n", "the key 'name' is required")]
    [InlineData("name: x\nstream: sometimes\nsteps: []\n", "'stream' must be allowed, never or required")]
    [InlineData("name: x\nversion: one\nsteps: []\n", "'version' must be an integer")]
    [InlineData("name: x\nversion: 0\nsteps: []\n", "'version' must be an integer of 1 or higher")]
    [InlineData("name: x\nsteps:\n  - id: a\n", "needs the key 'uses'")]
    [InlineData("name: x\nsteps:\n  - uses: a\n    with: text\n", "'with' of step 1 must be a mapping")]
    [InlineData("name: x\nsteps: nope\n", "'steps' must be a list")]
    [InlineData("name: x\ninput: 5\nsteps: []\n", "'input' must be a mapping")]
    [InlineData("- a\n- b\n", "must hold a YAML mapping")]
    [InlineData("name: x\nstream: 1\nsteps: []\n", "'stream' must be text")]
    [InlineData("name: 5\nsteps: []\n", "'name' must be text")]
    [InlineData("name: x\nsummary: 7\nsteps: []\n", "'summary' must be text")]
    [InlineData("name: x\nsteps:\n  - uses: 3\n", "'uses' of step 1 must be text")]
    public void Mistake_IsReported(string yaml, string expected)
    {
        var result = YamlPipelineReader.Read(yaml, "f.yaml");

        Assert.False(result.IsSuccess);
        Assert.Contains(expected, result.Error!.Message, StringComparison.Ordinal);
        Assert.StartsWith("f.yaml", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>A file that is not valid YAML gives an error with the file name.</summary>
    [Fact]
    public void BrokenYaml_IsReported()
    {
        var result = YamlPipelineReader.Read("name: [unclosed\n", "broken.yaml");

        Assert.False(result.IsSuccess);
        Assert.Contains("broken.yaml: the YAML is not valid", result.Error!.Message, StringComparison.Ordinal);
    }
}
