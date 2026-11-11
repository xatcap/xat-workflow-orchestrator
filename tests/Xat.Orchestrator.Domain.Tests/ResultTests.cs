using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Domain.Tests;

/// <summary>Tests the behaviour of <see cref="Result"/> and <see cref="Result{T}"/>.</summary>
public class ResultTests
{
    /// <summary>A sample error that the tests use.</summary>
    private static readonly PipelineError _sampleError = new("sample.failed", "The sample failed.", ErrorKind.Validation);

    /// <summary>A successful result has no error.</summary>
    [Fact]
    public void Ok_HasNoError()
    {
        var result = Result.Ok();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    /// <summary>A failed result holds its error.</summary>
    [Fact]
    public void Fail_HoldsTheError()
    {
        var result = Result.Fail(_sampleError);

        Assert.False(result.IsSuccess);
        Assert.Same(_sampleError, result.Error);
    }

    /// <summary>A successful generic result holds its value and no error.</summary>
    [Fact]
    public void GenericOk_HoldsTheValue()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    /// <summary>A failed generic result holds its error and no value.</summary>
    [Fact]
    public void GenericFail_HoldsTheErrorAndNoValue()
    {
        var result = Result<string>.Fail(_sampleError);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Same(_sampleError, result.Error);
    }

    /// <summary>The factory methods reject a null error.</summary>
    [Fact]
    public void Fail_WithNullError_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Fail(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Fail(null!));
    }

    /// <summary>The generic <c>Ok</c> method rejects a null value.</summary>
    [Fact]
    public void GenericOk_WithNullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Ok(null!));
    }

    /// <summary>A default non-generic result is a success, because it means that nothing went wrong.</summary>
    [Fact]
    public void DefaultResult_IsASuccess()
    {
        var result = default(Result);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A default generic result has no value, so it must be a failure with a known error.</summary>
    [Fact]
    public void DefaultGenericResult_IsAFailureWithTheUninitializedError()
    {
        var result = default(Result<string>);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Equal("result.uninitialized", result.Error.Code);
    }

    /// <summary>The text of a result never shows the value, so a log line cannot leak a payload.</summary>
    [Fact]
    public void ToString_NeverShowsTheValue()
    {
        Assert.Equal("Ok", Result<string>.Ok("secret-payload").ToString());
        Assert.Equal("Fail(sample.failed)", Result<string>.Fail(_sampleError).ToString());
    }
}
