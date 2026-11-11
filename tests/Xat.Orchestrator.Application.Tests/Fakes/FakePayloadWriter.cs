using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>Writes a short text body that has the run ID, the state and the webhook state.</summary>
internal sealed class FakePayloadWriter : IWebhookPayloadWriter
{
    /// <summary>Gets or sets a flag that makes the writer throw, so that a test can check a failure outside the sender.</summary>
    public bool Throw { get; set; }

    public string Write(RunRecord record) =>
        Throw ? throw new InvalidOperationException("writer failure") : $"{record.RunId}|{record.State}|{record.Webhook.State}";
}
