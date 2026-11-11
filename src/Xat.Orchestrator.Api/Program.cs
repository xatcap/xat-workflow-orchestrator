using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Api;
using Xat.Orchestrator.Api.Endpoints;
using Xat.Orchestrator.Api.Ingestion;
using Xat.Orchestrator.Api.OpenAi;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure;
using Xat.Orchestrator.Infrastructure.Definitions;

// The content root is the folder of the program, not the current folder.
// So the program finds appsettings.json, the pipeline files and the prompt templates from any folder.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The orchestrator has no authentication. Refuse to start on a network address.
// The "urls" setting holds the addresses from --urls, ASPNETCORE_URLS, DOTNET_URLS and appsettings.json.
// The host fills app.Urls only when it starts, so this first check reads the setting.
try
{
    ListenAddressPolicy.EnsureLoopbackOnly(ListenAddressPolicy.SplitUrls(builder.Configuration["urls"]));
}
catch (InvalidOperationException exception)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}

var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = api.MaxRequestBodyBytes);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton<IWebhookPayloadWriter, WebhookPayloadWriter>();
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection(IngestionOptions.SectionName));
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<IngestionForwarder>();
builder.Services.AddHostedService<AsyncRunWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Admission control. This protects the upstream services from more work than they can queue. It is not authorization.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
    limiter.AddConcurrencyLimiter(RunRules.RateLimitPolicy, concurrency =>
    {
        concurrency.PermitLimit = api.MaxConcurrentRuns;
        concurrency.QueueLimit = api.QueueLimit;
        concurrency.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    limiter.OnRejected = (context, ct) => new ValueTask(ApiErrors.WriteAsync(
        context.HttpContext,
        new PipelineError("admission.full", "The orchestrator has no free capacity. Try again later.", ErrorKind.Overloaded, TimeSpan.FromSeconds(1)),
        null,
        ct));
});

var app = builder.Build();

// Fail fast. A bad pipeline file, template file or setting must stop the start, not fail a request later.
try
{
    var pipelines = app.Services.GetRequiredService<IPipelineCatalog>();
    _ = app.Services.GetRequiredService<IModelCatalog>();
    var ingestion = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>() ?? new IngestionOptions();
    var apiProblems = api.Validate(pipelines).Concat(ingestion.Validate()).ToList();
    if (apiProblems.Count > 0)
    {
        foreach (var problem in apiProblems)
        {
            await Console.Error.WriteLineAsync(problem);
        }

        return 1;
    }
}
catch (ConfigurationFilesException exception)
{
    foreach (var problem in exception.Problems)
    {
        await Console.Error.WriteLineAsync(problem);
    }

    return 1;
}
catch (OptionsValidationException exception)
{
    foreach (var failure in exception.Failures)
    {
        await Console.Error.WriteLineAsync(failure);
    }

    return 1;
}

// This second check reads the addresses that the server really bound. It also covers the Kestrel:Endpoints setting.
var refusedAddress = false;
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        ListenAddressPolicy.EnsureLoopbackOnly(app.Urls);
    }
    catch (InvalidOperationException exception)
    {
        refusedAddress = true;
        StartupLog.NonLoopbackAddress(app.Logger, exception);
        app.Lifetime.StopApplication();
    }
});

app.UseMiddleware<BrowserOriginGuard>();
app.UseExceptionHandler();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapRunEndpoints();
app.MapRunStatusEndpoints();
app.MapIngestionEndpoints();
app.MapChatCompletionsEndpoint();

await app.RunAsync();

// A refused address is a configuration error, so the process must not report success to a supervisor.
return refusedAddress ? 1 : 0;

/// <summary>The entry point. The class is public so that the test host can reach it.</summary>
public partial class Program;
