using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>Checks the settings of the ingestion proxy when the service starts.</summary>
internal sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        var failures = new List<string>();
        OptionRules.Check(options, IngestionOptions.SectionName, failures);
        failures.AddRange(options.DisabledRoutes
            .Where(route => !IngestionRoutes.Names.Contains(route, StringComparer.Ordinal))
            .Select(route => $"{IngestionOptions.SectionName}:DisabledRoutes names the route '{route}', which does not exist. "
                + $"Known routes: {string.Join(", ", IngestionRoutes.Names)}."));
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
