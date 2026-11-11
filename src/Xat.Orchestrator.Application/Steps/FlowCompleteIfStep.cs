using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>Ends the run early with a fixed answer when a condition is true. Use it for the "no answer" path.</summary>
public sealed class FlowCompleteIfStep : IPipelineStep<FlowCompleteIfOptions>, IStepValidator<FlowCompleteIfOptions>
{
    /// <inheritdoc/>
    public ValueTask<StepOutcome> ExecuteAsync(StepContext context, FlowCompleteIfOptions options, CancellationToken ct)
    {
        context.Run.State.TryLookup(options.Key!, out var value);

        var matches = options.IsEmpty is { } wantEmpty
            ? IsEmptyValue(value) == wantEmpty
            : string.Equals(AsText(value), options.EqualsValue, StringComparison.Ordinal);

        return ValueTask.FromResult(matches
            ? StepOutcome.Complete(new Answer(options.Text!, []), RunStatus.NoAnswer)
            : StepOutcome.Continue);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(FlowCompleteIfOptions options, StepValidationContext context)
    {
        var messages = new List<string>();
        if ((options.IsEmpty is null) == (options.EqualsValue is null))
        {
            messages.Add("Set exactly one of 'isEmpty' and 'equals'.");
        }

        var parts = (options.Key ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || (parts[0] != "input" && parts[0] != "steps"))
        {
            messages.Add("The key must start with 'input.' or 'steps.'.");
        }
        else if (parts[0] == "steps" && (parts.Length != 3 || !context.EarlierStepIds.Contains(parts[1])))
        {
            messages.Add("The key must have the form steps.<id>.<name> and name an earlier step.");
        }

        return messages;
    }

    private static bool IsEmptyValue(object? value) =>
        value switch
        {
            null => true,
            string text => text.Length == 0,
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => true,
                JsonValueKind.String => element.GetString()!.Length == 0,
                JsonValueKind.Array => element.GetArrayLength() == 0,
                JsonValueKind.Object => !element.EnumerateObject().Any(),
                _ => false,
            },
            IEnumerable items => !items.Cast<object>().Any(),
            _ => false,
        };

    private static string? AsText(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
}
