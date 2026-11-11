using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Domain.Errors;
using Xat.Orchestrator.Infrastructure.Options;

namespace Xat.Orchestrator.Infrastructure.Discovery;

/// <summary>The names of the upstream services, for error codes and messages.</summary>
internal static class UpstreamNames
{
    /// <summary>Gets the name of an upstream, for example <c>xat-rag</c>.</summary>
    public static string Of(Upstream upstream) => upstream == Upstream.XatRag ? "xat-rag" : "xat-llm-engine";
}
