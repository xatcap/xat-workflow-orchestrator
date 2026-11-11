using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Api.Contracts.Catalog;
using Xat.Orchestrator.Api.Contracts.Runs;
using Xat.Orchestrator.Api.Errors;
using Xat.Orchestrator.Application.Abstractions.Pipelines;
using Xat.Orchestrator.Application.Abstractions.Retrieval;
using Xat.Orchestrator.Application.Engine.Registry;
using Xat.Orchestrator.Application.Engine.StepContracts;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>
/// The routes that describe what the orchestrator offers. A new pipeline file or retrieval alias shows here
/// after a restart, with no code change. This is how a caller finds new features.
/// </summary>
internal static class CatalogEndpoints
{
    /// <summary>Maps the catalog routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/pipelines", (IPipelineCatalog catalog) =>
            Results.Json(
                new { Pipelines = catalog.All.OrderBy(pipeline => pipeline.Name, StringComparer.Ordinal).Select(Describe).ToList() },
                ApiJson.Options));

        app.MapGet("/v1/pipelines/{name}", async (string name, IPipelineCatalog catalog, HttpContext http) =>
        {
            var pipeline = catalog.Find(name);
            if (!pipeline.IsSuccess)
            {
                await ApiErrors.WriteAsync(http, pipeline.Error, null, http.RequestAborted);
                return;
            }

            await http.Response.WriteAsJsonAsync(Describe(pipeline.Value), ApiJson.Options, "application/json", http.RequestAborted);
        });

        app.MapGet("/v1/steps", (IStepRegistry registry) =>
            Results.Json(
                new
                {
                    Steps = registry.All
                        .OrderBy(step => step.Name, StringComparer.Ordinal)
                        .Select(step => new StepTypeDto(step.Name, step.Summary, TraitNames(step.Traits)))
                        .ToList(),
                },
                ApiJson.Options));

        app.MapGet("/v1/contexts", (IRetrievalProfiles profiles) =>
        {
            var contexts = profiles.All
                .OrderBy(profile => profile.Alias, StringComparer.Ordinal)
                .Select(profile => new ContextDto(profile.Alias, profile.Context, profile.Chunks))
                .ToList();
            return Results.Json(new { Contexts = contexts }, ApiJson.Options);
        });

        return app;
    }

    private static PipelineDescriptorDto Describe(PipelineDefinition pipeline) =>
        new(
            pipeline.Name,
            pipeline.Version,
            pipeline.Summary,
            pipeline.Stream.ToString().ToLowerInvariant(),
            pipeline.InputSchema,
            pipeline.Steps.Select(step => step.Uses).ToList());

    private static List<string> TraitNames(StepTraits traits) =>
        Enum.GetValues<StepTraits>()
            .Where(trait => trait != StepTraits.None && traits.HasFlag(trait))
            .Select(trait => trait.ToString())
            .ToList();
}
