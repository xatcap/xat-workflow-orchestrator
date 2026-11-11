using Xat.Orchestrator.Application.Abstractions.Runs;
using Xat.Orchestrator.Domain.Errors;

namespace Xat.Orchestrator.Application.Tests.Fakes;

/// <summary>A webhook policy that allows every address except one on the host <c>blocked.test</c>.</summary>
internal sealed class FakeWebhookPolicy : IWebhookPolicy
{
    public Result<Uri> Check(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host != "blocked.test"
            ? Result<Uri>.Ok(uri)
            : Result<Uri>.Fail(new PipelineError("webhook.host_not_allowed", "The webhook host is not allowed.", ErrorKind.Validation));
}
