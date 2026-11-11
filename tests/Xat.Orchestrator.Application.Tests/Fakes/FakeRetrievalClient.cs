using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xat.Orchestrator.Application;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Application.Engine;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Pipelines;
using Xat.Orchestrator.Domain.Retrieval;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>An in-memory fake of xat-rag. A test sets the result and reads the queries.</summary>
internal sealed class FakeRetrievalClient : IRetrievalClient
{
    public List<RetrievalQuery> Queries { get; } = [];

    public Func<RetrievalQuery, Result<RetrievedSet>> OnRetrieve { get; set; } =
        query => Result<RetrievedSet>.Ok(new RetrievedSet([TestData.Chunk("c1", "Orders", "The orders service uses port 8080.")], 0));

    public Task<Result<RetrievedSet>> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        return Task.FromResult(OnRetrieve(query));
    }

    public Task<Result<RetrievalCatalog>> GetCatalogAsync(CancellationToken ct) =>
        Task.FromResult(Result<RetrievalCatalog>.Ok(new RetrievalCatalog(["ask-all", "ask-docs"], ["docs"])));
}
