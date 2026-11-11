using System.Text.Json;
using Xat.Orchestrator.Api.OpenAi;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>Tests which pipelines the model list offers to a chat client.</summary>
public class ChatCompletionCallableTests
{
    private static PipelineDefinition Pipeline(string schema, StreamMode stream = StreamMode.Allowed)
    {
        using var document = JsonDocument.Parse(schema);
        return new PipelineDefinition("p", 1, string.Empty, stream, document.RootElement.Clone(), []);
    }

    /// <summary>A pipeline is callable when it has the input prompt and needs no other input, and it does not always stream.</summary>
    [Theory]
    [InlineData("""{"properties":{"prompt":{"type":"string"}},"required":["prompt"]}""", StreamMode.Allowed, true)]
    [InlineData("""{"properties":{"prompt":{},"system_prompt":{}},"required":["prompt","system_prompt"]}""", StreamMode.Never, true)]
    [InlineData("""{"properties":{"prompt":{}}}""", StreamMode.Allowed, true)]
    [InlineData("""{"properties":{"prompt":{},"context":{}},"required":["prompt","context"]}""", StreamMode.Allowed, false)]
    [InlineData("""{"properties":{"question":{}},"required":["question"]}""", StreamMode.Allowed, false)]
    [InlineData("""{"properties":{"prompt":{}},"required":["prompt"]}""", StreamMode.Required, false)]
    [InlineData("""{}""", StreamMode.Allowed, false)]
    public void IsCallable_FollowsTheRules(string schema, StreamMode stream, bool expected)
    {
        Assert.Equal(expected, ChatCompletionRequestReader.IsCallable(Pipeline(schema, stream)));
    }
}
