using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>A model alias.</summary>
/// <param name="Alias">The alias that pipelines use.</param>
/// <param name="Model">The model ID in the form <c>provider/model</c>.</param>
/// <param name="ContextWindow">The context window in tokens.</param>
/// <param name="SupportsGrammar">Shows whether the model accepts a grammar.</param>
/// <param name="Available"><see langword="true"/> when the engine lists the model, <see langword="false"/> when it does not, <see langword="null"/> when the engine is not reachable.</param>
internal sealed record ModelDto(string Alias, string Model, int ContextWindow, bool SupportsGrammar, bool? Available);
