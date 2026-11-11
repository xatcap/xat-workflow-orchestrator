using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>Builds a service provider with the real engine and steps, and fake ports.</summary>
internal static class TestHost
{
    public static ServiceProvider Build(
        FakeRetrievalClient? rag = null,
        FakeLlmClient? llm = null,
        int contextWindow = 8192,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddSingleton<IRetrievalClient>(rag ?? new FakeRetrievalClient());
        services.AddSingleton<ILlmClient>(llm ?? new FakeLlmClient());
        services.AddSingleton<IModelCatalog>(new StubModelCatalog(contextWindow));
        services.AddSingleton<IRetrievalProfiles, StubProfiles>();
        services.AddSingleton<IPromptTemplateStore, StubTemplates>();
        services.AddSingleton<ITokenCounter, StubTokenCounter>();
        services.Configure<RunnerOptions>(options => options.RunTimeout = TimeSpan.FromSeconds(30));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
