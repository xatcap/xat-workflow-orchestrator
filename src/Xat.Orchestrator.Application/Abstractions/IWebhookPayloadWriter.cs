using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>Writes the JSON body of a webhook. The host implements it, so the webhook has the same form as the public API.</summary>
public interface IWebhookPayloadWriter
{
    /// <summary>Writes the body for a run.</summary>
    /// <param name="record">The run.</param>
    /// <returns>The JSON text.</returns>
    string Write(RunRecord record);
}
