using System.Diagnostics.CodeAnalysis;

namespace Xat.Orchestrator.Domain.Errors;

/// <summary>The outcome of an operation that has no value. An expected failure is a value, not an exception.</summary>
/// <remarks>A default instance is a success. Use <see cref="Ok"/> and <see cref="Fail"/> to make results.</remarks>
public readonly record struct Result
{
    /// <summary>The error of a failed result, or <see langword="null"/> for a success.</summary>
    private readonly PipelineError? _error;

    /// <summary>Initializes a new instance of the <see cref="Result"/> struct.</summary>
    /// <param name="error">The error of the failure, or <see langword="null"/> for a success.</param>
    private Result(PipelineError? error) => _error = error;

    /// <summary>Gets a value that shows whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _error is null;

    /// <summary>Gets the error, or <see langword="null"/> when the operation succeeded.</summary>
    public PipelineError? Error => _error;

    /// <summary>Makes a successful result.</summary>
    /// <returns>A result that has no error.</returns>
    public static Result Ok() => new(null);

    /// <summary>Makes a failed result.</summary>
    /// <param name="error">The error of the failure.</param>
    /// <returns>A result that holds the error.</returns>
    /// <exception cref="ArgumentNullException">The <paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Fail(PipelineError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }
}
