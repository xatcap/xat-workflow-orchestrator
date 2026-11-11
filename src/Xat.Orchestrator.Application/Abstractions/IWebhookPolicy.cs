using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Abstractions;

/// <summary>
/// The rule that decides to which addresses the orchestrator can send webhooks. Without this rule, a caller could make
/// the orchestrator send a request to any address. The rule is not authentication.
/// </summary>
public interface IWebhookPolicy
{
    /// <summary>Checks an address.</summary>
    /// <param name="url">The address that the caller gave.</param>
    /// <returns>The parsed address, or a validation error. The error never repeats the address.</returns>
    Result<Uri> Check(string url);
}
