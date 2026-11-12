using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;
using Xat.Orchestrator.Application;

namespace Xat.Orchestrator.Infrastructure.Tests.Support;

/// <summary>
/// Starts two WireMock servers as fake xat-rag and fake xat-llm-engine, and builds a service provider with the real adapters
/// that point to them.
/// </summary>
internal sealed class Harness : IDisposable
{
    public Harness(Dictionary<string, string?>? settings = null, bool withApplication = false)
    {
        Rag = WireMockServer.Start();
        Engine = WireMockServer.Start();

        var values = new Dictionary<string, string?>
        {
            ["Upstreams:XatRag:BaseUrl"] = Rag.Url,
            ["Upstreams:XatRag:RetryCount"] = "0",
            ["Upstreams:XatRag:RequestTimeoutSeconds"] = "10",
            ["Upstreams:XatLlmEngine:BaseUrl"] = Engine.Url,
            ["Upstreams:XatLlmEngine:RetryCount"] = "0",
            ["Upstreams:XatLlmEngine:RequestTimeoutSeconds"] = "10",
            ["Retrieval:Profiles:default:Context"] = "ask-all",
            ["Llm:Models:default:Model"] = "local/test-model",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (withApplication)
        {
            services.AddApplication(Configuration);
        }

        services.AddInfrastructure(Configuration);
        Services = services.BuildServiceProvider();
    }

    public WireMockServer Rag { get; }

    public WireMockServer Engine { get; }

    public IConfiguration Configuration { get; }

    public ServiceProvider Services { get; }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        Services.Dispose();
        Rag.Dispose();
        Engine.Dispose();
    }
}
