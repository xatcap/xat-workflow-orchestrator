using Xat.Orchestrator.Api.Endpoints;
using Xat.Orchestrator.Api.Hosting;
using Xat.Orchestrator.Api.Ingestion;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Infrastructure;

// The content root is the folder of the program, not the current folder.
// So the program finds appsettings.json from any folder. This setting wins over --contentRoot and ASPNETCORE_CONTENTROOT.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The orchestrator has no authentication. Refuse to start on a network address. This check reads the settings before the server binds.
try
{
    ListenAddressPolicy.EnsureLoopbackOnly(builder.Configuration);
}
catch (InvalidOperationException exception)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}

builder.Services
    .AddApplication(builder.Configuration)
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// Fail fast. A bad pipeline file, template file or setting must stop the start, not fail a request later.
var problems = StartupValidation.Run(app.Services);
if (problems.Count > 0)
{
    foreach (var problem in problems)
    {
        await Console.Error.WriteLineAsync(problem);
    }

    return 1;
}

// This second check reads the addresses that the server really bound. It catches a source that the first check does not know.
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

await app.RunAsync();

// A refused address is a configuration error, so the process must not report success to a supervisor.
return refusedAddress ? 1 : 0;

/// <summary>The entry point. The class is public so that the test host can reach it.</summary>
public partial class Program;
