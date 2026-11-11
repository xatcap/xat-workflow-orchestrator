using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Application.Tests.Fakes;

namespace Xat.Orchestrator.Application.Tests;

/// <summary>Tests the JSON Schema subset that checks the input of a caller.</summary>
public class InputSchemaValidatorTests
{
    private const string Schema = """
        {"type":"object","required":["prompt"],"additionalProperties":false,
         "properties":{
           "prompt":{"type":"string","minLength":2,"maxLength":10},
           "n":{"type":"integer","minimum":1,"maximum":5},
           "mode":{"type":"string","enum":["a","b"]},
           "tags":{"type":"array","maxItems":2,"items":{"type":"string"}}}}
        """;

    private static IReadOnlyList<string> Check(string inputJson) =>
        InputSchemaValidator.Validate(TestData.Json(Schema), TestData.Json(inputJson));

    /// <summary>A valid input gives no message.</summary>
    [Fact]
    public void ValidInput_HasNoProblem()
    {
        Assert.Empty(Check("""{"prompt":"hello","n":3,"mode":"a","tags":["x"]}"""));
    }

    /// <summary>Each rule gives its own message with the path.</summary>
    [Theory]
    [InlineData("""{}""", "input.prompt: the property is required.")]
    [InlineData("""{"prompt":5}""", "input.prompt: the type is not valid. Expected string.")]
    [InlineData("""{"prompt":"x"}""", "input.prompt: the length is too small. The minimum is 2 characters.")]
    [InlineData("""{"prompt":"hello world!"}""", "input.prompt: the length is too large. The maximum is 10 characters.")]
    [InlineData("""{"prompt":"ok","n":1.5}""", "input.n: the type is not valid. Expected integer.")]
    [InlineData("""{"prompt":"ok","n":9}""", "input.n: the number is too large. The maximum is 5.")]
    [InlineData("""{"prompt":"ok","mode":"z"}""", "input.mode: the value is not one of the allowed values.")]
    [InlineData("""{"prompt":"ok","tags":["a","b","c"]}""", "input.tags: the length is too large. The maximum is 2 items.")]
    [InlineData("""{"prompt":"ok","tags":[1]}""", "input.tags[0]: the type is not valid. Expected string.")]
    [InlineData("""{"prompt":"ok","extra":1}""", "input.extra: the property is not allowed.")]
    public void InvalidInput_GivesAMessageWithThePathAndTheRule(string input, string expected)
    {
        Assert.Contains(expected, Check(input));
    }

    /// <summary>A message never repeats the value of the caller.</summary>
    [Fact]
    public void Messages_NeverRepeatTheInputValue()
    {
        var messages = Check("""{"prompt":"secret-value-that-is-too-long"}""");

        Assert.NotEmpty(messages);
        Assert.DoesNotContain(messages, message => message.Contains("secret", StringComparison.Ordinal));
    }

    /// <summary>An undefined schema accepts any input.</summary>
    [Fact]
    public void UndefinedSchema_AcceptsAnything()
    {
        Assert.Empty(InputSchemaValidator.Validate(default, TestData.Json("""{"anything":true}""")));
    }

    /// <summary>The schema check rejects a keyword that the validator cannot enforce.</summary>
    [Theory]
    [InlineData("""{"$ref":"#/x"}""")]
    [InlineData("""{"properties":{"a":{"oneOf":[]}}}""")]
    [InlineData("""{"pattern":"^a"}""")]
    [InlineData("""{"additionalProperties":true}""")]
    public void CheckSchema_RejectsUnsupportedKeywords(string schema)
    {
        Assert.NotEmpty(InputSchemaValidator.CheckSchema(TestData.Json(schema)));
    }

    /// <summary>The notes in a schema are allowed.</summary>
    [Fact]
    public void CheckSchema_AllowsDescriptionsAndTitles()
    {
        Assert.Empty(InputSchemaValidator.CheckSchema(TestData.Json("""{"title":"t","description":"d","type":"object","properties":{"a":{"description":"x","type":"string"}}}""")));
    }
}
