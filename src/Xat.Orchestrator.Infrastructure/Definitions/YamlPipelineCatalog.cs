using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Definitions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Infrastructure.Options.Runtime;

namespace Xat.Orchestrator.Infrastructure.Definitions;

/// <summary>
/// Loads all pipelines from the YAML files of a folder and checks them. A new file in the folder is a new pipeline,
/// and it shows in the list of pipelines after a restart. The files are read once, at the first use.
/// The start calls <see cref="Validate"/>, and it stops when a file is not valid.
/// </summary>
internal sealed class YamlPipelineCatalog(IOptions<PipelinesOptions> options, PipelineDefinitionValidator validator) : IPipelineCatalog
{
    private readonly Lazy<Loaded> _loaded = new(() => Load(options.Value.Folder, validator));

    /// <inheritdoc/>
    public IReadOnlyCollection<PipelineDefinition> All => Pipelines().Values;

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate() => _loaded.Value.Problems;

    /// <inheritdoc/>
    public Result<PipelineDefinition> Find(string name) =>
        Pipelines().TryGetValue(name, out var pipeline)
            ? Result<PipelineDefinition>.Ok(pipeline)
            : Result<PipelineDefinition>.Fail(new PipelineError(
                "pipeline.not_found", "The pipeline does not exist.", ErrorKind.NotFound));

    /// <summary>Gets the pipelines. The start checks the files first, so a request never sees a catalog with problems.</summary>
    /// <exception cref="ConfigurationFilesException">A file or a definition is not valid.</exception>
    private Dictionary<string, PipelineDefinition> Pipelines() =>
        _loaded.Value.Problems.Count == 0 ? _loaded.Value.Pipelines : throw new ConfigurationFilesException(_loaded.Value.Problems);

    private static Loaded Load(string configuredFolder, PipelineDefinitionValidator validator)
    {
        var folder = Path.IsPathRooted(configuredFolder) ? configuredFolder : Path.Combine(AppContext.BaseDirectory, configuredFolder);
        var pipelines = new Dictionary<string, PipelineDefinition>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return new Loaded(pipelines, [$"The pipeline folder '{configuredFolder}' does not exist."]);
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
            if (!pipelines.TryAdd(read.Value.Name, read.Value))
            {
                problems.Add($"{Path.GetFileName(file)}: a pipeline with the name '{read.Value.Name}' exists already.");
            }
        }

        if (problems.Count == 0 && pipelines.Count == 0)
        {
            problems.Add($"The pipeline folder '{configuredFolder}' has no pipeline file.");
        }

        return new Loaded(pipelines, problems);
    }

    private sealed record Loaded(Dictionary<string, PipelineDefinition> Pipelines, IReadOnlyList<string> Problems);
}
