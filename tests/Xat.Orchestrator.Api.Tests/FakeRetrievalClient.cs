using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Api.Tests;

/// <summary>A fake of xat-rag. A test sets the result and reads the queries.</summary>
internal sealed class FakeRetrievalClient : IRetrievalClient
{
    public List<RetrievalQuery> Queries { get; } = [];

    public Func<RetrievalQuery, Result<RetrievedSet>> OnRetrieve { get; set; } = query =>
        Result<RetrievedSet>.Ok(new RetrievedSet([Chunk(query.Context + "-1", "Orders", "The orders service uses port 8080.")], 0));

    public static Chunk Chunk(string id, string title, string text) =>
        new(id, null, "section", 1.0, new UntrustedText("Section"), new UntrustedText(text), new ChunkSource("docs", id, "markdown", $"https://example.test/{id}", title, "1"));

    public Task<Result<RetrievedSet>> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        lock (Queries)
        {
            Queries.Add(query);
        }

        return Task.FromResult(OnRetrieve(query));
    }
}
