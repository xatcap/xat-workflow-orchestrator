namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>
/// The ingestion proxy. It forwards the ingestion routes of xat-rag, so a caller needs to know only one address.
/// The proxy has no authentication, by decision of the owner. The routes that delete or rebuild are on by default,
/// and an operator can turn each route off with <c>Ingestion:DisabledRoutes</c>.
/// </summary>
internal static class IngestionEndpoints
{
    /// <summary>Maps the ingestion routes.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        Map(app, "GET", "/v1/rags", IngestionRoutes.ListRags);
        Map(app, "GET", $"/v1/rags/{IngestionRoutes.RagName}", IngestionRoutes.GetRag);
        Map(app, "PUT", $"/v1/rags/{IngestionRoutes.RagName}/documents", IngestionRoutes.UploadDocuments, isUpload: true);
        Map(app, "DELETE", $"/v1/rags/{IngestionRoutes.RagName}/documents/{{**docId}}", IngestionRoutes.DeleteDocument, keepRawPath: true);
        Map(app, "POST", $"/v1/rags/{IngestionRoutes.RagName}/rebuild", IngestionRoutes.RebuildRag);
        Map(app, "GET", "/v1/jobs/{jobId:regex(^[A-Za-z0-9_-]{{1,64}}$)}", IngestionRoutes.GetJob);
        Map(app, "DELETE", "/v1/jobs/{jobId:regex(^[A-Za-z0-9_-]{{1,64}}$)}", IngestionRoutes.CancelJob);
        return app;
    }

    private static void Map(IEndpointRouteBuilder app, string method, string pattern, string route, bool isUpload = false, bool keepRawPath = false) =>
        app.MapMethods(pattern, [method], (HttpContext http, IngestionForwarder forwarder) => forwarder.ForwardAsync(http, route, isUpload, keepRawPath))
            .WithName(route);
}
