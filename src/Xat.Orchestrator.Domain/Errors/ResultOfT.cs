using System.Diagnostics.CodeAnalysis;

namespace Xat.Orchestrator.Domain.Errors;

/// <summary>The outcome of an operation that returns a value. An expected failure is a value, not an exception.</summary>
/// <remarks>
/// A default instance is a failure with the error code <c>result.uninitialized</c>, because a default instance has no value.
/// The <see cref="ToString"/> method never prints the value, so a log line cannot leak a payload.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The call form Result<T>.Ok(value) is the form that the repository guidelines use for result types.")]
public readonly record struct Result<T>
{
    /// <summary>The error of a result that was never set. A default instance returns this error.</summary>
    private static readonly PipelineError _uninitialized = new(
        "result.uninitialized",
        "The result has no value, because it was not created with Ok or Fail.",
        ErrorKind.UpstreamFailed);

    /// <summary>The value of a successful result.</summary>
    private readonly T? _value;

    /// <summary>The error of a failed result, or <see langword="null"/> for a success and for a default instance.</summary>
    private readonly PipelineError? _error;

    /// <summary>A flag that only <see cref="Ok"/> sets. A default instance has the value <see langword="false"/>.</summary>
    private readonly bool _isSuccess;

    /// <summary>Initializes a new instance of the <see cref="Result{T}"/> struct.</summary>
    /// <param name="value">The value of a success.</param>
    /// <param name="error">The error of a failure.</param>
    /// <param name="isSuccess">A value that shows whether the result is a success.</param>
    private Result(T? value, PipelineError? error, bool isSuccess)
    {
        _value = value;
        _error = error;
        _isSuccess = isSuccess;
    }

    /// <summary>Gets a value that shows whether the operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    /// <summary>Gets the value, or <see langword="default"/> when the operation failed.</summary>
    public T? Value => _value;

    /// <summary>Gets the error, or <see langword="null"/> when the operation succeeded.</summary>
    public PipelineError? Error => _isSuccess ? null : _error ?? _uninitialized;

    /// <summary>Makes a successful result.</summary>
    /// <param name="value">The value of the result. The value must not be <see langword="null"/>.</param>
    /// <returns>A result that holds the value.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="value"/> is <see langword="null"/>.</exception>
    public static Result<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(value, null, isSuccess: true);
    }

    /// <summary>Makes a failed result.</summary>
    /// <param name="error">The error of the failure.</param>
    /// <returns>A result that holds the error.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Fail(PipelineError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error, isSuccess: false);
    }

    /// <summary>Gets a text that shows the state of the result and never shows the value.</summary>
    /// <returns>The text <c>Ok</c>, or the text <c>Fail</c> followed by the error code.</returns>
    public override string ToString() => _isSuccess ? "Ok" : $"Fail({Error!.Code})";
}
