using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>A step type that a pipeline file can use by name.</summary>
public interface IStepRegistration
{
    /// <summary>Gets the registered name, for example <c>rag.retrieve</c>.</summary>
    string Name { get; }

    /// <summary>Gets the type of the options.</summary>
    Type OptionsType { get; }

    /// <summary>Gets the properties of the step type.</summary>
    StepTraits Traits { get; }

    /// <summary>Gets a short description for the list of steps.</summary>
    string Summary { get; }

    /// <summary>Checks the <c>with</c> block of one use of the step when the service starts.</summary>
    /// <param name="stepOptions">The options as the pipeline file defines them.</param>
    /// <param name="context">The data for the check.</param>
    /// <returns>The messages about wrong options. An empty list means that the options are valid.</returns>
    IReadOnlyList<string> Validate(JsonElement stepOptions, StepValidationContext context);

    /// <summary>Binds the options and runs the step.</summary>
    /// <param name="context">The run and the step.</param>
    /// <param name="resolvedWith">The options, after the engine resolved the expressions.</param>
    /// <param name="ct">The token that cancels the step.</param>
    /// <returns>The outcome of the step.</returns>
    ValueTask<StepOutcome> ExecuteAsync(StepContext context, JsonElement resolvedWith, CancellationToken ct);
}
