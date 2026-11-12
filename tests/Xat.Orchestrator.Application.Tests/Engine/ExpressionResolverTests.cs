using System.Text.Json;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Application.Tests.Fakes;

namespace Xat.Orchestrator.Application.Tests.Engine;

/// <summary>Tests the <c>${...}</c> expressions in the options of a step.</summary>
public class ExpressionResolverTests
{
    private static PipelineState State(string inputJson = """{"prompt":"hello","n":3,"nested":{"a":["x","y"]}}""") =>
        new(TestData.Json(inputJson));

    private static string Resolve(string withJson, PipelineState? state = null)
    {
        var result = ExpressionResolver.Resolve(TestData.Json(withJson), state ?? State());
        Assert.True(result.IsSuccess);
        return result.Value.GetRawText();
    }

    /// <summary>A text that is one expression keeps the type of the value.</summary>
    [Fact]
    public void WholeExpression_KeepsTheType()
    {
        Assert.Equal("""{"n":3}""", Resolve("""{"n":"${input.n}"}"""));
    }

    /// <summary>An expression inside a longer text becomes text.</summary>
    [Fact]
    public void EmbeddedExpression_BecomesText()
    {
        Assert.Equal("""{"t":"say hello twice: hello"}""", Resolve("""{"t":"say ${input.prompt} twice: ${input.prompt}"}"""));
    }

    /// <summary>A missing value without a default becomes null.</summary>
    [Fact]
    public void MissingValue_BecomesNull()
    {
        Assert.Equal("""{"t":null}""", Resolve("""{"t":"${input.nothing}"}"""));
    }

    /// <summary>A missing value with a default uses the default. A quoted default loses its quotes.</summary>
    [Theory]
    [InlineData("""{"t":"${input.nothing ?? 'fallback'}"}""", """{"t":"fallback"}""")]
    [InlineData("""{"t":"${input.prompt ?? 'fallback'}"}""", """{"t":"hello"}""")]
    public void Default_IsUsedOnlyWhenTheValueIsMissing(string with, string expected)
    {
        Assert.Equal(expected, Resolve(with));
    }

    /// <summary>A path can go into objects and arrays.</summary>
    [Fact]
    public void Path_ReachesNestedValues()
    {
        Assert.Equal("""{"t":"y"}""", Resolve("""{"t":"${input.nested.a.1}"}"""));
    }

    /// <summary>A text without an expression stays the same, and so do numbers and nested objects.</summary>
    [Fact]
    public void PlainValues_AreNotChanged()
    {
        Assert.Equal("""{"a":"text","b":2,"c":{"d":[1,"x"]}}""", Resolve("""{"a":"text","b":2,"c":{"d":[1,"x"]}}"""));
    }

    /// <summary>The value of a step result can be used when it is a simple value.</summary>
    [Fact]
    public void StepOutput_CanBeRead()
    {
        var state = State();
        state.SetOutput("retrieve", StepOutputs.Count, 4);

        Assert.Equal("""{"n":4}""", Resolve("""{"n":"${steps.retrieve.count}"}""", state));
    }

    /// <summary>A step result that is not a simple value cannot be used in an expression.</summary>
    [Fact]
    public void StepOutput_OfAComplexType_IsAnError()
    {
        var state = State();
        state.SetOutput("retrieve", new StateKey<List<int>>("chunks"), [1]);

        var result = ExpressionResolver.Resolve(TestData.Json("""{"n":"${steps.retrieve.chunks}"}"""), state);

        Assert.False(result.IsSuccess);
        Assert.Equal("expression.invalid", result.Error.Code);
    }

    /// <summary>The validator finds the paths of all expressions, also in nested values.</summary>
    [Fact]
    public void FindPaths_ReturnsAllPathsWithoutDefaults()
    {
        var paths = ExpressionResolver.FindPaths(TestData.Json("""{"a":"${input.x ?? 'd'}","b":["${steps.s.out}"],"c":{"d":"x ${input.y} z"}}"""));

        Assert.Equal(["input.x", "steps.s.out", "input.y"], paths);
    }

    /// <summary>An undefined options block resolves to an empty object.</summary>
    [Fact]
    public void UndefinedOptions_BecomeAnEmptyObject()
    {
        var result = ExpressionResolver.Resolve(default, State());

        Assert.True(result.IsSuccess);
        Assert.Equal(JsonValueKind.Object, result.Value.ValueKind);
    }
}
