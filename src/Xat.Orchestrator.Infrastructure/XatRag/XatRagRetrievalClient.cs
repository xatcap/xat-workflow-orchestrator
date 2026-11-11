using System.Net.Http.Json;
using System.Text.Json;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Retrieval;
using Xat.Orchestrator.Infrastructure.Discovery;

namespace Xat.Orchestrator.Infrastructure.XatRag;

/// <summary>
/// The adapter to xat-rag. It calls <c>POST /v1/context</c> and <c>GET /v1/rags</c>, and it turns the answers into domain types.
/// The chunk text goes into <see cref="UntrustedText"/>, because the text of a document can hold instructions that an attacker wrote.
/// </summary>
internal sealed class XatRagRetrievalClient(HttpClient http) : IRetrievalClient
{
    private const string Name = "xat-rag";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc/>
    public async Task<Result<RetrievedSet>> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            using var response = await http.PostAsJsonAsync(
                "/v1/context", new ContextRequest(query.Context, query.Question, query.Chunks), _json, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<RetrievedSet>.Fail(await UpstreamErrors.FromResponseAsync(response, Name, ct));
            }

            var body = await response.Content.ReadFromJsonAsync<ContextResponse>(_json, ct);
            if (body is null)
            {
                return Result<RetrievedSet>.Fail(new PipelineError(
                    $"{Name}.invalid_response", $"{Name} sent an empty answer.", ErrorKind.UpstreamFailed, Source: Name));
            }

            var chunks = (body.Chunks ?? []).Select(ToDomain).ToList();
            return Result<RetrievedSet>.Ok(new RetrievedSet(chunks, body.Omitted?.Count ?? 0));
        }
        catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error))
        {
            return Result<RetrievedSet>.Fail(error);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<RetrievalCatalog>> GetCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("/v1/rags", ct);
            if (!response.IsSuccessStatusCode)
            {
                return Result<RetrievalCatalog>.Fail(await UpstreamErrors.FromResponseAsync(response, Name, ct));
            }

            var body = await response.Content.ReadFromJsonAsync<CatalogResponse>(_json, ct);
            var contexts = body?.Contexts ?? [];
            var rags = (body?.Rags ?? []).Select(rag => rag.Name).ToList();
            return Result<RetrievalCatalog>.Ok(new RetrievalCatalog(contexts, rags));
        }
        catch (Exception exception) when (UpstreamErrors.TryMap(exception, Name, ct, out var error))
        {
            return Result<RetrievalCatalog>.Fail(error);
        }
    }

    private static Chunk ToDomain(ChunkDto chunk) =>
        new(
            chunk.ChunkId,
            chunk.ParentId,
            chunk.Kind ?? string.Empty,
            chunk.Score,
            new UntrustedText(chunk.Header ?? string.Empty),
            new UntrustedText(chunk.Text ?? string.Empty),
            new ChunkSource(
                chunk.Source?.Rag ?? string.Empty,
                chunk.Source?.DocId ?? string.Empty,
                chunk.Source?.SourceKind ?? string.Empty,
                chunk.Source?.Uri ?? string.Empty,
                chunk.Source?.Title ?? string.Empty,
                chunk.Source?.Version ?? string.Empty));

    private sealed record ContextRequest(string Context, string Question, int Chunks);

    private sealed record ContextResponse(IReadOnlyList<ChunkDto>? Chunks, IReadOnlyList<OmittedDto>? Omitted);

    private sealed record ChunkDto(string ChunkId, string? ParentId, string? Kind, double Score, string? Header, string? Text, SourceDto? Source);

    private sealed record SourceDto(string? Rag, string? DocId, string? SourceKind, string? Uri, string? Title, string? Version);

    private sealed record OmittedDto(string? ChunkId, string? Reason);

    private sealed record CatalogResponse(IReadOnlyList<RagDto>? Rags, IReadOnlyList<string>? Contexts);

    private sealed record RagDto(string Name);
}
