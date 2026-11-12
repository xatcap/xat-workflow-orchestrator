using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Configuration;

namespace Xat.Orchestrator.Api.Hosting;

/// <summary>Checks the API settings when the service starts. The default pipeline must exist.</summary>
/// <param name="pipelines">The pipelines.</param>
internal sealed class ApiOptionsValidator(IPipelineCatalog pipelines) : IValidateOptions<ApiOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        var failures = new List<string>();
        OptionRules.Check(options, ApiOptions.SectionName, failures);
        if (!pipelines.Find(options.DefaultPipeline).IsSuccess)
        {
            failures.Add($"{ApiOptions.SectionName}:DefaultPipeline names the pipeline '{options.DefaultPipeline}', which does not exist.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
