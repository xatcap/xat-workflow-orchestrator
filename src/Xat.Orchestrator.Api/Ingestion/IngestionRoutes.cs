namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>
/// The only xat-rag routes that the proxy forwards. The proxy never forwards another path, so a caller cannot reach
/// the other routes of xat-rag, such as the debug search, through the orchestrator.
/// </summary>
internal static class IngestionRoutes
{
    /// <summary>The route that lists the RAGs and the contexts.</summary>
    public const string ListRags = "list-rags";

    /// <summary>The route that shows the counts and the manifest of one RAG.</summary>
    public const string GetRag = "get-rag";

    /// <summary>The route that uploads documents.</summary>
    public const string UploadDocuments = "upload-documents";

    /// <summary>The route that deletes one document.</summary>
    public const string DeleteDocument = "delete-document";

    /// <summary>The route that deletes the index of a RAG and makes an empty one.</summary>
    public const string RebuildRag = "rebuild-rag";

    /// <summary>The route that shows an ingestion job.</summary>
    public const string GetJob = "get-ingestion-job";

    /// <summary>The route that asks an ingestion job to stop.</summary>
    public const string CancelJob = "cancel-ingestion-job";

    /// <summary>The pattern of a RAG name in xat-rag. A route constraint ignores the case by default, so the pattern turns this off.</summary>
    public const string RagName = "{rag:regex(^(?-i:[a-z0-9][a-z0-9-]{{0,63}})$)}";

    /// <summary>Gets the names of all routes.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [ListRags, GetRag, UploadDocuments, DeleteDocument, RebuildRag, GetJob, CancelJob];
}
