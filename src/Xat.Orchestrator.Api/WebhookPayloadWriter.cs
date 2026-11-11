using System.Text.Json;
using Xat.Orchestrator.Api.Contracts;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api;

/// <summary>Writes the body of a webhook with the same JSON rules and the same run form as the public API.</summary>
internal sealed class WebhookPayloadWriter : IWebhookPayloadWriter
{
    /// <inheritdoc/>
    public string Write(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(new WebhookEnvelope("run.finished", RunStatusDto.From(record)), ApiJson.Options);
    }
}
