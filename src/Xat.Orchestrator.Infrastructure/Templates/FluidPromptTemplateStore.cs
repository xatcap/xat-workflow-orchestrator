using Fluid;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Templates;

/// <summary>
/// Loads the prompt templates from <c>name@version.liquid</c> files when the service starts. A template that cannot be parsed
/// stops the start. The templates use the Liquid language. Fluid allows no access to .NET objects, so a template is safe to edit.
/// </summary>
internal sealed class FluidPromptTemplateStore : IPromptTemplateStore
{
    private static readonly FluidParser _parser = new();

    private readonly Dictionary<string, IPromptTemplate> _templates = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="FluidPromptTemplateStore"/> class.</summary>
    /// <param name="options">The options with the template folder.</param>
    /// <exception cref="ConfigurationFilesException">A template file cannot be parsed.</exception>
    public FluidPromptTemplateStore(IOptions<PromptsOptions> options)
    {
        var folder = Path.IsPathRooted(options.Value.Folder)
            ? options.Value.Folder
            : Path.Combine(AppContext.BaseDirectory, options.Value.Folder);

        if (!Directory.Exists(folder))
        {
            // A deployment without templates is valid. A step that names a template then reports the missing template.
            return;
        }

        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.liquid").Order(StringComparer.Ordinal))
        {
            var reference = Path.GetFileNameWithoutExtension(file);
            if (_parser.TryParse(File.ReadAllText(file), out var template, out var error))
            {
                _templates[reference] = new FluidTemplate(template);
            }
            else
            {
                problems.Add($"The template '{reference}' is not valid. {error}");
            }
        }

        if (problems.Count > 0)
        {
            throw new ConfigurationFilesException(problems);
        }
    }

    /// <inheritdoc/>
    public Result<IPromptTemplate> Find(string reference) =>
        _templates.TryGetValue(reference, out var template)
            ? Result<IPromptTemplate>.Ok(template)
            : Result<IPromptTemplate>.Fail(new PipelineError(
                "template.unknown",
                $"The prompt template '{reference}' does not exist. A template file is named name@version.liquid.",
                ErrorKind.NotFound));

    private sealed class FluidTemplate(IFluidTemplate template) : IPromptTemplate
    {
        public string Render(IReadOnlyDictionary<string, object?> model)
        {
            var context = new TemplateContext();
            foreach (var (key, value) in model)
            {
                context.SetValue(key, value);
            }

            return template.Render(context);
        }
    }
}
