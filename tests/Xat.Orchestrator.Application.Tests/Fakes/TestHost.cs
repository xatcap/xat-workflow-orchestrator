using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Engine.Execution;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>Builds a service provider with the real engine and steps, and fake ports.</summary>
internal static class TestHost
{
    public static ServiceProvider Build(
        FakeRetrievalClient? rag = null,
        FakeLlmClient? llm = null,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddSingleton<IRetrievalClient>(rag ?? new FakeRetrievalClient());
        services.AddSingleton<ILlmClient>(llm ?? new FakeLlmClient());
        services.AddSingleton<IModelCatalog>(new StubModelCatalog());
        services.AddSingleton<IRetrievalProfiles, StubProfiles>();
        services.AddSingleton<IPromptTemplateStore, StubTemplates>();
        services.Configure<RunnerOptions>(options => options.RunTimeout = TimeSpan.FromSeconds(30));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
