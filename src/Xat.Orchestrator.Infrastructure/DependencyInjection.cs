using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Xat.Orchestrator.Application.Abstractions.Llm;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Infrastructure.Catalogs;
using Xat.Orchestrator.Infrastructure.Definitions;
using Xat.Orchestrator.Infrastructure.Discovery;
using Xat.Orchestrator.Infrastructure.Health;
using Xat.Orchestrator.Infrastructure.Jobs;
using Xat.Orchestrator.Infrastructure.Options.Llm;
using Xat.Orchestrator.Infrastructure.Options.Retrieval;
using Xat.Orchestrator.Infrastructure.Options.Runtime;
using Xat.Orchestrator.Infrastructure.Options.Upstream;
using Xat.Orchestrator.Infrastructure.Options.Webhooks;
using Xat.Orchestrator.Infrastructure.Templates;
using Xat.Orchestrator.Infrastructure.Webhooks;
using Xat.Orchestrator.Infrastructure.XatLlmEngine;
using Xat.Orchestrator.Infrastructure.XatRag;

namespace Xat.Orchestrator.Infrastructure;

/// <summary>Registers the adapters of the Infrastructure layer.</summary>
public static class DependencyInjection
{
    internal const string XatRagClient = "xat-rag";
    internal const string XatLlmEngineClient = "xat-llm-engine";
    internal const string XatRagHealthClient = "xat-rag-health";
    internal const string XatLlmEngineHealthClient = "xat-llm-engine-health";
    internal const string WebhookClient = "webhooks";

    /// <summary>Adds the adapters, the catalogs and the settings to the service collection.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AddOptions(services, configuration);

        services.AddSingleton<IUpstreamLocator, ReadyFileLocator>();
        services.AddSingleton(provider => new XatRagEndpoint(provider.GetRequiredService<IUpstreamLocator>()));
        services.AddSingleton<IRetrievalProfiles, ConfigRetrievalProfiles>();
        services.AddSingleton<IModelCatalog, ConfigModelCatalog>();
        services.AddSingleton<IPromptTemplateStore, FluidPromptTemplateStore>();
        services.AddSingleton<IPipelineCatalog, YamlPipelineCatalog>();
        services.AddSingleton<IRunStore, InMemoryRunStore>();
        services.AddSingleton<IWebhookPolicy, WebhookPolicy>();
        services.AddSingleton<IWebhookSender, HttpWebhookSender>();

        // The webhook client never follows a redirect, because a redirect can send the request to an address that the policy refused.
        // The client has no cookie store, because a cookie from one receiver must not go to another request.
        // It has no HTTP logging, because the default log writes the full address, and the path can hold a secret.
        services.AddHttpClient(WebhookClient, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers();

        var upstreams = configuration.GetSection(UpstreamOptions.SectionName).Get<UpstreamOptions>() ?? new UpstreamOptions();
        AddUpstreamClients(services, upstreams);

        services.AddHealthChecks()
            .AddCheck<XatRagHealthCheck>("xat-rag", HealthStatus.Degraded, ["ready"])
            .AddCheck<XatLlmEngineHealthCheck>("xat-llm-engine", HealthStatus.Degraded, ["ready"]);

        return services;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UpstreamOptions>().Bind(configuration.GetSection(UpstreamOptions.SectionName)).ValidateOnStart();
        services.AddOptions<RetrievalOptions>().Bind(configuration.GetSection(RetrievalOptions.SectionName)).ValidateOnStart();

        // A key that the code does not know stops the start. So a wrong or old key cannot stay in a file and do nothing.
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
            .ValidateOnStart();

        services.AddOptions<PipelinesOptions>().Bind(configuration.GetSection(PipelinesOptions.SectionName));
        services.AddOptions<PromptsOptions>().Bind(configuration.GetSection(PromptsOptions.SectionName));
        services.AddOptions<WebhookOptions>().Bind(configuration.GetSection(WebhookOptions.SectionName)).ValidateOnStart();

        services.AddSingleton<IValidateOptions<UpstreamOptions>, UpstreamOptionsValidator>();
        services.AddSingleton<IValidateOptions<RetrievalOptions>, RetrievalOptionsValidator>();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();
        services.AddSingleton<IValidateOptions<WebhookOptions>, WebhookOptionsValidator>();
    }

    private static void AddUpstreamClients(IServiceCollection services, UpstreamOptions upstreams)
    {
        var rag = services.AddHttpClient(XatRagClient, client =>
        {
            client.BaseAddress = new Uri("http://xat-rag.invalid/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // The order of the handlers matters. The resilience handler is first, so it is the outer handler.
        // The address handler is inside it, so each retry finds the current address again.
        rag.AddResilienceHandler("xat-rag", builder => ConfigureResilience(builder, upstreams.XatRag));
        rag.AddHttpMessageHandler(provider => new UpstreamAddressHandler(provider.GetRequiredService<IUpstreamLocator>(), Upstream.XatRag));

        var llm = services.AddHttpClient(XatLlmEngineClient, client =>
        {
            client.BaseAddress = new Uri("http://xat-llm-engine.invalid/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        llm.AddResilienceHandler("xat-llm-engine", builder => ConfigureResilience(builder, upstreams.XatLlmEngine));
        llm.AddHttpMessageHandler(provider => new UpstreamAddressHandler(provider.GetRequiredService<IUpstreamLocator>(), Upstream.XatLlmEngine));

        // The adapters are singletons that get a client from the factory for each call. The steps that use them are singletons too.
        // The LLM adapter sits behind the limit for each model.
        services.AddSingleton<IRetrievalClient, XatRagRetrievalClient>();
        services.AddSingleton<XatEngineLlmClient>();
        services.AddSingleton<ILlmClient>(provider => new ParallelLimitedLlmClient(
            provider.GetRequiredService<XatEngineLlmClient>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ParallelLimitedLlmClient>>()));

        // The health clients have no retry. A health check must answer fast, also when the service is down.
        services.AddHttpClient(XatRagHealthClient, client => client.BaseAddress = new Uri("http://xat-rag.invalid/"))
            .AddHttpMessageHandler(provider => new UpstreamAddressHandler(provider.GetRequiredService<IUpstreamLocator>(), Upstream.XatRag));
        services.AddHttpClient(XatLlmEngineHealthClient, client => client.BaseAddress = new Uri("http://xat-llm-engine.invalid/"))
            .AddHttpMessageHandler(provider => new UpstreamAddressHandler(provider.GetRequiredService<IUpstreamLocator>(), Upstream.XatLlmEngine));
    }

    /// <summary>
    /// Sets the retry and the time limit of one upstream. Neither upstream retries by itself, so the orchestrator retries only
    /// after a 429, 503 or 504 answer or a failed connection. The retry uses the <c>Retry-After</c> header. The time limit covers all tries.
    /// </summary>
    private static void ConfigureResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder, UpstreamEndpointOptions endpoint)
    {
        builder.AddTimeout(TimeSpan.FromSeconds(endpoint.RequestTimeoutSeconds));

        if (endpoint.RetryCount > 0)
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = endpoint.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                UseJitter = true,
                ShouldRetryAfterHeader = true,
                ShouldHandle = arguments => ValueTask.FromResult(IsTransient(arguments.Outcome)),
            });
        }
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode is System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout
        || outcome.Exception is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError };
}
