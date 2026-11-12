using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Infrastructure.Definitions;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>
/// Checks the pipeline files, the template files and all settings before the server starts. A mistake must stop the start,
/// not fail a request later. The check gives plain messages, so the operator sees no stack trace.
/// </summary>
internal static class StartupValidation
{
    /// <summary>Runs all checks.</summary>
    /// <param name="services">The services of the built host.</param>
    /// <returns>The problems. An empty list means that the service can start.</returns>
    public static IReadOnlyList<string> Run(IServiceProvider services)
    {
        try
        {
            var pipelineProblems = services.GetRequiredService<IPipelineCatalog>().Validate();
            if (pipelineProblems.Count > 0)
            {
                return pipelineProblems;
            }

            services.GetRequiredService<IStartupValidator>().Validate();
            return [];
        }
        catch (ConfigurationFilesException exception)
        {
            return exception.Problems;
        }
        catch (OptionsValidationException exception)
        {
            return exception.Failures.ToList();
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            // The start validator collects the errors of all sections. More than one failed section comes as one aggregate.
            return exception.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures).ToList();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            return [$"The service cannot start. {exception.GetType().Name}: {exception.Message}"];
        }
    }
}
