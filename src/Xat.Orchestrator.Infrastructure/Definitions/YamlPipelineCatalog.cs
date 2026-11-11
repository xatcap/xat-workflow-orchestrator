using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Definitions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Options.Runtime;

namespace Xat.Orchestrator.Infrastructure.Definitions;

/// <summary>
/// Loads all pipelines from the YAML files of a folder and checks them. A new file in the folder is a new pipeline,
/// and it shows in the list of pipelines after a restart. The service does not start when a file is not valid.
/// </summary>
internal sealed class YamlPipelineCatalog : IPipelineCatalog
{
    private readonly Dictionary<string, PipelineDefinition> _pipelines = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="YamlPipelineCatalog"/> class.</summary>
    /// <param name="options">The options with the pipeline folder.</param>
    /// <param name="validator">The validator for the definitions.</param>
    /// <exception cref="ConfigurationFilesException">A file or a definition is not valid.</exception>
    public YamlPipelineCatalog(IOptions<PipelinesOptions> options, PipelineDefinitionValidator validator)
    {
        var folder = Path.IsPathRooted(options.Value.Folder)
            ? options.Value.Folder
            : Path.Combine(AppContext.BaseDirectory, options.Value.Folder);

        if (!Directory.Exists(folder))
        {
            throw new ConfigurationFilesException([$"The pipeline folder '{options.Value.Folder}' does not exist."]);
        }

        var problems = new List<string>();
        var files = Directory.EnumerateFiles(folder, "*.*")
            .Where(file => file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var read = YamlPipelineReader.Read(File.ReadAllText(file), Path.GetFileName(file));
            if (!read.IsSuccess)
            {
                problems.Add(read.Error.Message);
                continue;
            }

            problems.AddRange(validator.Validate(read.Value));
            if (!_pipelines.TryAdd(read.Value.Name, read.Value))
            {
                problems.Add($"{Path.GetFileName(file)}: a pipeline with the name '{read.Value.Name}' exists already.");
            }
        }

        if (problems.Count == 0 && _pipelines.Count == 0)
        {
            problems.Add($"The pipeline folder '{options.Value.Folder}' has no pipeline file.");
        }

        if (problems.Count > 0)
        {
            throw new ConfigurationFilesException(problems);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<PipelineDefinition> All => _pipelines.Values;

    /// <inheritdoc/>
    public Result<PipelineDefinition> Find(string name) =>
        _pipelines.TryGetValue(name, out var pipeline)
            ? Result<PipelineDefinition>.Ok(pipeline)
            : Result<PipelineDefinition>.Fail(new PipelineError(
                "pipeline.not_found", "The pipeline does not exist.", ErrorKind.NotFound));
}
