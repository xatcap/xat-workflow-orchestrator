using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts.Errors;

/// <summary>The body of every error response. The form is the same as the form of the upstream services.</summary>
/// <param name="Error">The error.</param>
internal sealed record ErrorEnvelope(ErrorBody Error);
