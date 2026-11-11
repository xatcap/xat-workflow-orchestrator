using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts.Runs;

/// <summary>A retrieval alias.</summary>
/// <param name="Alias">The alias that callers and pipelines use.</param>
/// <param name="Context">The name of the xat-rag context.</param>
/// <param name="Chunks">The default number of chunks.</param>
internal sealed record ContextDto(string Alias, string Context, int Chunks);
