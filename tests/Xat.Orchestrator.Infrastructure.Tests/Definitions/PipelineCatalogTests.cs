using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Engine.Execution;
using Xat.Orchestrator.Application.Engine.Inputs;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Options.Upstream;

namespace Xat.Orchestrator.Infrastructure.Tests.Definitions;

/// <summary>Tests the catalog of pipeline files and the files that the repository ships.</summary>
public sealed class PipelineCatalogTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("xat-pipelines-").FullName;

    /// <inheritdoc/>
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XatOrchestrator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The solution folder was not found.");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, bool useShippedSettings)
    {
        var builder = new ConfigurationBuilder();
        if (useShippedSettings)
        {
            builder.AddJsonFile(Path.Combine(RepositoryRoot(), "src", "Xat.Orchestrator.Api", "appsettings.json"), optional: false);
        }

        // The start checks the model and retrieval settings, so every build needs at least one of each.
        var baseline = new Dictionary<string, string?>
        {
            ["Llm:Models:default:Model"] = "local/test-model",
            ["Retrieval:Profiles:default:Context"] = "ask-all",
        };
        foreach (var (key, value) in settings)
        {
            baseline[key] = value;
        }

        var configuration = builder.AddInMemoryCollection(useShippedSettings ? settings : baseline).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>The pipeline files, the prompt templates and appsettings.json that the repository ships work together. The check starts every pipeline validation.</summary>
    [Fact]
    public void ShippedPipelines_AreValid_WithTheShippedSettings()
    {
        var root = RepositoryRoot();
        using var services = Build(
            new Dictionary<string, string?>
            {
                ["Pipelines:Folder"] = Path.Combine(root, "pipelines"),
                ["Prompts:Folder"] = Path.Combine(root, "prompts"),
            },
            useShippedSettings: true);

        var catalog = services.GetRequiredService<IPipelineCatalog>();

        Assert.Equal(
            ["chat", "custom-prompt", "rag-qa", "rag-sequential"],
            catalog.All.Select(pipeline => pipeline.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The shipped settings use the documented xat-rag address. The xat-llm-engine timeout is shorter than the run timeout,
    /// so a slow model gives the engine timeout error and not the run timeout error.
    /// </summary>
    [Fact]
    public void ShippedSettings_HaveTheDocumentedDefaults()
    {
        using var services = Build([], useShippedSettings: true);

        var upstreams = services.GetRequiredService<IOptions<UpstreamOptions>>().Value;
        var runs = services.GetRequiredService<IOptions<RunnerOptions>>().Value;

        Assert.Equal("http://127.0.0.1:8421", upstreams.XatRag.BaseUrl);
        Assert.True(TimeSpan.FromSeconds(upstreams.XatLlmEngine.RequestTimeoutSeconds) < runs.RunTimeout);
        Assert.True(TimeSpan.FromSeconds(upstreams.XatRag.RequestTimeoutSeconds) < runs.RunTimeout);
    }

    /// <summary>The catalog reads .yml files, so the build must copy them too. Otherwise such a pipeline is missing after a build.</summary>
    [Fact]
    public void Build_CopiesYamlAndYmlPipelineFiles()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Xat.Orchestrator.Api", "Xat.Orchestrator.Api.csproj"));
        File.WriteAllText(Path.Combine(_folder, "extra.yml"), "name: extra\nsteps:\n  - uses: prompt.user\n    with: { text: hi }\n  - uses: prompt.assemble\n  - uses: llm.generate\n");
        using var services = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = _folder }, useShippedSettings: false);

        Assert.Contains(@"pipelines\**\*.yaml", project, StringComparison.Ordinal);
        Assert.Contains(@"pipelines\**\*.yml", project, StringComparison.Ordinal);
        Assert.True(services.GetRequiredService<IPipelineCatalog>().Find("extra").IsSuccess);
    }

    /// <summary>A new file in the folder is a new pipeline. No code changes.</summary>
    [Fact]
    public void NewFile_IsANewPipeline()
    {
        File.WriteAllText(Path.Combine(_folder, "extra.yaml"), "name: extra\nsteps:\n  - uses: prompt.user\n    with: { text: hi }\n  - uses: prompt.assemble\n  - uses: llm.generate\n");
        using var services = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = _folder }, useShippedSettings: false);

        var catalog = services.GetRequiredService<IPipelineCatalog>();

        Assert.True(catalog.Find("extra").IsSuccess);
        Assert.Equal(ErrorKind.NotFound, catalog.Find("missing").Error!.Kind);
    }

    /// <summary>One bad file stops the start. The error names all problems of all files.</summary>
    [Fact]
    public void BadFiles_StopTheStart_WithAllProblems()
    {
        File.WriteAllText(Path.Combine(_folder, "a.yaml"), "name: a\nsteps:\n  - uses: nope.step\n");
        File.WriteAllText(Path.Combine(_folder, "b.yaml"), "name: b\nsteepz: 1\nsteps: []\n");
        using var services = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = _folder }, useShippedSettings: false);

        var problems = services.GetRequiredService<IPipelineCatalog>().Validate();

        Assert.Contains(problems, problem => problem.Contains("unknown step type 'nope.step'", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("b.yaml", StringComparison.Ordinal));
        Assert.Throws<ConfigurationFilesException>(() => services.GetRequiredService<IPipelineCatalog>().All);
    }

    /// <summary>Two files with the same pipeline name are not allowed.</summary>
    [Fact]
    public void DuplicateNames_StopTheStart()
    {
        var content = "name: same\nsteps:\n  - uses: prompt.user\n    with: { text: hi }\n  - uses: prompt.assemble\n  - uses: llm.generate\n";
        File.WriteAllText(Path.Combine(_folder, "one.yaml"), content);
        File.WriteAllText(Path.Combine(_folder, "two.yaml"), content);
        using var services = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = _folder }, useShippedSettings: false);

        var problems = services.GetRequiredService<IPipelineCatalog>().Validate();

        Assert.Contains(problems, problem => problem.Contains("exists already", StringComparison.Ordinal));
    }

    /// <summary>A missing or empty folder stops the start with a clear message.</summary>
    [Fact]
    public void MissingOrEmptyFolder_StopsTheStart()
    {
        using var services = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = _folder }, useShippedSettings: false);

        var empty = services.GetRequiredService<IPipelineCatalog>().Validate();
        Assert.Contains(empty, problem => problem.Contains("has no pipeline file", StringComparison.Ordinal));

        using var other = Build(new Dictionary<string, string?> { ["Pipelines:Folder"] = Path.Combine(_folder, "nope") }, useShippedSettings: false);
        var missing = other.GetRequiredService<IPipelineCatalog>().Validate();
        Assert.Contains(missing, problem => problem.Contains("does not exist", StringComparison.Ordinal));
    }

    /// <summary>A template with a syntax error stops the start. A valid template renders with the input.</summary>
    [Fact]
    public void Templates_AreParsedAtStart_AndRender()
    {
        File.WriteAllText(Path.Combine(_folder, "good@1.liquid"), "Hello {{ input.name }}!{% if input.loud %} LOUD{% endif %}");
        using (var services = Build(new Dictionary<string, string?> { ["Prompts:Folder"] = _folder }, useShippedSettings: false))
        {
            var template = services.GetRequiredService<IPromptTemplateStore>().Find("good@1").Value!;
            var input = JsonValues.ToObject(JsonSerializer.SerializeToElement(new { name = "Ada", loud = true }));
            Assert.Equal("Hello Ada! LOUD", template.Render(new Dictionary<string, object?> { ["input"] = input }));
            Assert.Equal(ErrorKind.Validation, services.GetRequiredService<IPromptTemplateStore>().Find("good@2").Error!.Kind);
        }

        File.WriteAllText(Path.Combine(_folder, "bad@1.liquid"), "{% if %}");
        using var broken = Build(new Dictionary<string, string?> { ["Prompts:Folder"] = _folder }, useShippedSettings: false);
        Assert.Throws<ConfigurationFilesException>(() => broken.GetRequiredService<IPromptTemplateStore>());
    }
}
