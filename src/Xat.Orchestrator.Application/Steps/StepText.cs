using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Application.Engine.State;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Steps;

/// <summary>Helpers that several steps share to get text from an option or a template.</summary>
internal static class StepText
{
    /// <summary>Checks that exactly one of the text and the template options is set, and that the template exists.</summary>
    public static IReadOnlyList<string> Validate(string? text, string? template, IPromptTemplateStore templates)
    {
        var hasText = text is not null;
        var hasTemplate = !string.IsNullOrWhiteSpace(template);
        if (hasText == hasTemplate)
        {
            return ["Set exactly one of 'text' and 'template'."];
        }

        if (!hasTemplate || ExpressionResolver.IsExpression(template))
        {
            return [];
        }

        // The name comes from the pipeline file here, not from a caller, so the message can name it.
        var found = templates.Find(template!);
        return found.IsSuccess ? [] : [$"{found.Error.Message} The template is '{template}'."];
    }

    /// <summary>Gets the text from the text option, or from the rendered template.</summary>
    public static Result<string> Resolve(IPromptTemplateStore templates, string? text, string? template, PipelineState state)
    {
        if (!string.IsNullOrWhiteSpace(template))
        {
            var found = templates.Find(template);
            if (!found.IsSuccess)
            {
                return Result<string>.Fail(found.Error);
            }

            var model = new Dictionary<string, object?> { ["input"] = JsonValues.ToObject(state.Input) };
            return Result<string>.Ok(found.Value.Render(model));
        }

        return Result<string>.Ok(text ?? string.Empty);
    }
}
