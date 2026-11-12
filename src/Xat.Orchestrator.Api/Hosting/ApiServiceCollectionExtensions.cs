using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Api.Endpoints;
using Xat.Orchestrator.Api.Errors;
using Xat.Orchestrator.Api.Ingestion;
using Xat.Orchestrator.Api.Runs;
using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>Registers the services of the Api layer.</summary>
internal static class ApiServiceCollectionExtensions
{
    /// <summary>Adds the settings, the HTTP services, the ingestion proxy and the background worker of the API.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>().Bind(configuration.GetSection(ApiOptions.SectionName)).ValidateOnStart();
        services.AddOptions<IngestionOptions>().Bind(configuration.GetSection(IngestionOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>();

        // The host sets up the server while it builds, before the start check runs. So the server reads the raw setting here.
        // A bad value then still reaches the start check, which prints a plain message.
        var section = configuration.GetSection(ApiOptions.SectionName);
        services.AddOptions<KestrelServerOptions>()
            .Configure(kestrel => kestrel.Limits.MaxRequestBodySize = (section.Get<ApiOptions>() ?? new ApiOptions()).MaxRequestBodyBytes);

        services.AddSingleton<IWebhookPayloadWriter, WebhookPayloadWriter>();
        services.AddSingleton<IListenAddresses, ServerListenAddresses>();
        services.AddHttpForwarder();
        services.AddSingleton<IIngestionTarget, XatRagIngestionTarget>();
        services.AddSingleton<IngestionForwarder>();
        services.AddHostedService<AsyncRunWorker>();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Admission control. This protects the upstream services from more work than they can queue. It is not authorization.
        services.AddRateLimiter(limiter => limiter.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable);
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<ApiOptions>>((limiter, api) =>
        {
            // The rate limiter is set up when the first request comes, after the start check. So the checked settings are safe here.
            limiter.AddConcurrencyLimiter(RunRules.RateLimitPolicy, concurrency =>
            {
                concurrency.PermitLimit = api.Value.MaxConcurrentRuns;
                concurrency.QueueLimit = api.Value.QueueLimit;
                concurrency.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
            limiter.OnRejected = (context, ct) => new ValueTask(ApiErrors.WriteAsync(
                context.HttpContext,
                new PipelineError("admission.full", "The orchestrator has no free capacity. Try again later.", ErrorKind.Overloaded, TimeSpan.FromSeconds(1)),
                null,
                ct));
        });

        return services;
    }
}
