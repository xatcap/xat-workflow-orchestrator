using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Xat.Orchestrator.Application.Engine;

/// <summary>The binding between a registered name and a step class.</summary>
internal sealed class StepRegistration<TStep, TOptions>(string name, StepTraits traits, string summary, TStep step) : IStepRegistration
    where TStep : class, IPipelineStep<TOptions>
    where TOptions : class
{
    public string Name { get; } = name;

    public Type OptionsType => typeof(TOptions);

    public StepTraits Traits { get; } = traits;

    public string Summary { get; } = summary;

    public IReadOnlyList<string> Validate(JsonElement stepOptions, StepValidationContext context)
    {
        var bound = StepOptionsBinder.Bind<TOptions>(stepOptions);
        if (!bound.IsSuccess)
        {
            return [bound.Error.Message];
        }

        return step is IStepValidator<TOptions> validator ? validator.Validate(bound.Value, context) : [];
    }

    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, JsonElement resolvedWith, CancellationToken ct)
    {
        var bound = StepOptionsBinder.Bind<TOptions>(resolvedWith);
        return bound.IsSuccess
            ? step.ExecuteAsync(context, bound.Value, ct)
            : ValueTask.FromResult(StepOutcome.Fail(bound.Error with { Source = context.Step.Id }));
    }
}
