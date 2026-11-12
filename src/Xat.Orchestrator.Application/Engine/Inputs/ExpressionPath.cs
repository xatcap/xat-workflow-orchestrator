namespace Xat.Orchestrator.Application.Engine.Inputs;

/// <summary>
/// The rules for a path in an expression or in a key option. A path starts with <c>input.</c> and names JSON properties,
/// or it has the form <c>steps.&lt;id&gt;.&lt;name&gt;</c> and names a result of an earlier step.
/// </summary>
public static class ExpressionPath
{
    /// <summary>Splits a path into its parts.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The parts, with no empty part.</returns>
    public static string[] Split(string? path) =>
        (path ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Checks a path when the service starts.</summary>
    /// <param name="path">The path.</param>
    /// <param name="earlierStepIds">The IDs of the steps that run before the step that uses the path.</param>
    /// <returns>The reason why the path is not valid, or <see langword="null"/> when it is valid.</returns>
    public static string? Check(string? path, IReadOnlySet<string> earlierStepIds)
    {
        ArgumentNullException.ThrowIfNull(earlierStepIds);

        var parts = Split(path);
        if (parts.Length == 0 || (parts[0] != "input" && parts[0] != "steps"))
        {
            return "must start with 'input.' or 'steps.'";
        }

        return parts[0] == "steps" && (parts.Length != 3 || !earlierStepIds.Contains(parts[1]))
            ? "must have the form steps.<id>.<name> and name an earlier step"
            : null;
    }
}
