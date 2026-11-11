using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Pipelines;

namespace Xat.Orchestrator.Api.Endpoints;

/// <summary>
/// The routes that describe what the orchestrator offers. A new pipeline file, retrieval alias or model alias shows here
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

        app.MapGet("/v1/contexts", async (IRetrievalProfiles profiles, IRetrievalClient retrieval, CancellationToken ct) =>
        {
            var catalog = await WithShortLimitAsync(limit => retrieval.GetCatalogAsync(limit), ct);
            var contexts = profiles.All
                .OrderBy(profile => profile.Alias, StringComparer.Ordinal)
                .Select(profile => new ContextDto(
                    profile.Alias,
                    profile.Context,
                    profile.Chunks,
                    catalog.IsSuccess ? catalog.Value.Contexts.Contains(profile.Context) : null))
                .ToList();
            return Results.Json(new { Contexts = contexts }, ApiJson.Options);
        });

        app.MapGet("/v1/models", async (IModelCatalog models, ILlmClient llm, IPipelineCatalog pipelines, CancellationToken ct) =>
        {
            var listed = await WithShortLimitAsync(limit => llm.ListModelsAsync(limit), ct);
            var items = models.All
                .OrderBy(model => model.Alias, StringComparer.Ordinal)
                .Select(model => new ModelDto(
                    model.Alias,
                    model.Model,
                    model.ContextWindow,
                    model.SupportsGrammar,
                    listed.IsSuccess ? listed.Value.Contains(model.Model) : null))
                .ToList();

            // The fields object and data follow the OpenAI list of models, so an OpenAI client can find the pipelines
            // that it can call with POST /v1/chat/completions. The fields default_model and models list the LLM aliases.
            var callable = pipelines.All
                .Where(OpenAi.ChatCompletionRequestReader.IsCallable)
                .OrderBy(pipeline => pipeline.Name, StringComparer.Ordinal)
                .Select(pipeline => new { Id = "pipeline/" + pipeline.Name, Object = "model", Created = 0, OwnedBy = "xat-workflow-orchestrator" })
                .ToList();
            return Results.Json(new { Object = "list", Data = callable, DefaultModel = models.DefaultAlias, Models = items }, ApiJson.Options);
        });

        return app;
    }

    /// <summary>
    /// Asks an upstream service for its list, with a short time limit. The lists only add the field <c>available</c>, so a slow
    /// or down service must not make the route slow. A timeout gives an unknown state, and the caller sees <c>available: null</c>.
    /// </summary>
    private static async Task<Result<T>> WithShortLimitAsync<T>(Func<CancellationToken, Task<Result<T>>> call, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await call(limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<T>.Fail(new PipelineError("upstream.slow", "The service did not answer in time.", ErrorKind.Timeout));
        }
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
