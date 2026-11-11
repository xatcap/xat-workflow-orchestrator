using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

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

        if (hasTemplate && !template!.Contains("${", StringComparison.Ordinal))
        {
            var found = templates.Find(template);
            if (!found.IsSuccess)
            {
                return [found.Error.Message];
            }
        }

        return [];
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
