using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Xat.Orchestrator.Application.Abstractions;
using Xat.Orchestrator.Domain.Errors;
using Yarp.ReverseProxy.Forwarder;

namespace Xat.Orchestrator.Api.Ingestion;

/// <summary>
/// Sends a request of the caller on to xat-rag and streams the answer back. The forwarder does not buffer the body,
/// so a large upload does not fill the memory of the orchestrator. The forwarder never follows a redirect.
/// </summary>
internal sealed class IngestionForwarder(IHttpForwarder forwarder, IXatRagEndpoint endpoint, IOptions<IngestionOptions> options)
{
    private static readonly HttpMessageInvoker _client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),

        // The handler adds the headers traceparent, tracestate and baggage by default. The caller can set their values,
        // so the proxy must not send them to xat-rag. The header allow-list runs before the handler, so it cannot remove them.
        ActivityHeadersPropagator = null,
        MaxConnectionsPerServer = 64,
    });

    /// <summary>Forwards a request. The response of xat-rag, with its status and body, goes to the caller as it is.</summary>
    /// <param name="http">The HTTP context of the caller.</param>
    /// <param name="route">The name of the route, from <see cref="IngestionRoutes"/>.</param>
    /// <param name="isUpload">Shows whether the route uploads documents. An upload can be larger than the normal body limit.</param>
    /// <param name="keepRawPath">Shows whether the proxy sends the original text of the path. The delete-document route needs this to keep the document ID exact.</param>
    /// <returns>A task that completes when the answer is sent.</returns>
    public async Task ForwardAsync(HttpContext http, string route, bool isUpload, bool keepRawPath = false)
    {
        var settings = options.Value;

        // The routes match with or without a trailing slash. The proxy forwards the path as it is, and xat-rag can answer a path with a slash
        // with a redirect to its own address. So the proxy accepts only the path without a trailing slash.
        if (http.Request.Path.Value is { Length: > 1 } path && path[^1] == '/')
        {
            await ApiErrors.WriteAsync(
                http,
                new PipelineError("ingestion.route_not_found", "The path must not end with a slash.", ErrorKind.NotFound),
                null,
                http.RequestAborted);
            return;
        }

        if (!settings.Enabled || settings.DisabledRoutes.Contains(route, StringComparer.Ordinal))
        {
            await ApiErrors.WriteAsync(
                http,
                new PipelineError("ingestion.route_disabled", "The operator turned this ingestion route off.", ErrorKind.NotFound),
                null,
                http.RequestAborted);
            return;
        }

        // An upload that declares a size above the limit is refused before xat-rag gets any part of it.
        // A body without a declared size is limited by the server while it is read.
        if (isUpload && http.Request.ContentLength is { } declared && declared > settings.MaxUploadBytes)
        {
            await ApiErrors.WriteAsync(
                http,
                new PipelineError("request.too_large", "The upload is larger than the limit.", ErrorKind.Validation),
                null,
                http.RequestAborted,
                StatusCodes.Status413PayloadTooLarge);
            return;
        }

        string? pathOverride = null;
        if (keepRawPath)
        {
            if (!IngestionRawPath.TryBuildDeletePath(http, (string)http.Request.RouteValues["rag"]!, out var rawPath))
            {
                await ApiErrors.WriteAsync(
                    http,
                    new PipelineError("ingestion.route_not_found", "The document ID is not valid.", ErrorKind.NotFound),
                    null,
                    http.RequestAborted);
                return;
            }

            pathOverride = rawPath;
        }

        var located = await endpoint.LocateAsync(http.RequestAborted);
        if (!located.IsSuccess)
        {
            await ApiErrors.WriteAsync(http, located.Error, null, http.RequestAborted);
            return;
        }

        if (isUpload && http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = settings.MaxUploadBytes;
        }

        var error = await forwarder.SendAsync(
            http,
            located.Value.ToString(),
            _client,
            new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(settings.ActivityTimeoutSeconds) },
            new IngestionTransformer(located.Value, pathOverride));

        if (error != ForwarderError.None)
        {
            await HandleErrorAsync(http, error);
        }
    }

    private async Task HandleErrorAsync(HttpContext http, ForwarderError error)
    {
        var exception = http.GetForwarderErrorFeature()?.Exception;
        if (http.Response.HasStarted)
        {
            // The answer started, so the forwarder ends it. The caller sees a cut response.
            return;
        }

        PipelineError mapped;
        int? status = null;
        if (FindBadRequest(exception) is { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            status = StatusCodes.Status413PayloadTooLarge;
            mapped = new PipelineError("request.too_large", "The upload is larger than the limit.", ErrorKind.Validation);
        }
        else if (error is ForwarderError.RequestBodyClient or ForwarderError.RequestCanceled)
        {
            // The caller left, cancelled, or sent a body that the server refused. YARP has set a status already.
            return;
        }
        else if (error == ForwarderError.Request && IsConnectionFailure(exception))
        {
            endpoint.ReportFailure();
            mapped = new PipelineError("xat-rag.unreachable", "xat-rag is not reachable.", ErrorKind.UpstreamUnavailable, Source: "xat-rag");
        }
        else if (error == ForwarderError.RequestTimedOut)
        {
            mapped = new PipelineError("xat-rag.timeout", "xat-rag did not answer in time.", ErrorKind.Timeout, Source: "xat-rag");
        }
        else
        {
            mapped = new PipelineError("xat-rag.proxy_failed", "The request to xat-rag failed.", ErrorKind.UpstreamFailed, Source: "xat-rag");
        }

        await ApiErrors.WriteAsync(http, mapped, null, http.RequestAborted, status);
    }

    /// <summary>Finds a bad-request exception of the server in an exception. The forwarder can wrap it in another exception.</summary>
    private static BadHttpRequestException? FindBadRequest(Exception? exception) =>
        exception switch
        {
            null => null,
            BadHttpRequestException bad => bad,
            AggregateException aggregate => aggregate.InnerExceptions.Select(FindBadRequest).FirstOrDefault(found => found is not null),
            _ => FindBadRequest(exception.InnerException),
        };

    /// <summary>Shows whether an exception is a failure to connect, not for example an answer that the proxy cannot read.</summary>
    private static bool IsConnectionFailure(Exception? exception) =>
        exception switch
        {
            null => true,
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError } => true,
            HttpRequestException { HttpRequestError: HttpRequestError.Unknown } http when http.InnerException is System.Net.Sockets.SocketException => true,
            _ => false,
        };
}
