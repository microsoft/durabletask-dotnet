// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using Microsoft.DurableTask.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

/// <summary>
/// Extension methods for making durable HTTP calls from orchestrator functions.
/// </summary>
public static class TaskOrchestrationContextHttpExtensions
{
    const int DefaultPollingIntervalMs = 30000;

    /// <summary>
    /// Makes a durable HTTP call. When <see cref="DurableHttpRequest.AsynchronousPatternEnabled"/> is
    /// <c>true</c> and the target returns HTTP 202 with a Location header, the framework will
    /// automatically poll until a non-202 response is received.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="request">The HTTP request to execute.</param>
    /// <returns>The HTTP response.</returns>
    public static async Task<DurableHttpResponse> CallHttpAsync(
        this TaskOrchestrationContext context, DurableHttpRequest request)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        ILogger logger = context.CreateReplaySafeLogger("Microsoft.DurableTask.Http.CallHttp");

        DurableHttpResponse response = await context.CallActivityAsync<DurableHttpResponse>(
            DurableTaskBuilderHttpExtensions.HttpTaskActivityName, request);

        // Handle 202 async polling pattern
        while (response.StatusCode == HttpStatusCode.Accepted && request.AsynchronousPatternEnabled)
        {
            if (response.Headers == null)
            {
                logger.LogWarning(
                    "HTTP response headers are null; unable to retrieve 'Location' URL for polling.");
                break;
            }

            // Determine polling delay
            DateTime fireAt;
            var headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase);

            if (headers.TryGetValue("Retry-After", out string? retryAfterStr)
                && int.TryParse(retryAfterStr, out int retryAfterSeconds))
            {
                fireAt = context.CurrentUtcDateTime.AddSeconds(retryAfterSeconds);
            }
            else
            {
                fireAt = context.CurrentUtcDateTime.AddMilliseconds(DefaultPollingIntervalMs);
            }

            await context.CreateTimer(fireAt, CancellationToken.None);

            // Get location URL
            if (!headers.TryGetValue("Location", out string? locationUrl) || locationUrl == null)
            {
                logger.LogWarning(
                    "HTTP 202 response missing 'Location' header; unable to poll for status.");
                break;
            }

            logger.LogInformation("Polling HTTP status at location: {LocationUrl}", locationUrl);

            // Build poll request: GET to Location URL with original headers
            var pollRequest = new DurableHttpRequest(HttpMethod.Get, new Uri(locationUrl))
            {
                Headers = request.Headers,
                AsynchronousPatternEnabled = request.AsynchronousPatternEnabled,
            };

            response = await context.CallActivityAsync<DurableHttpResponse>(
                DurableTaskBuilderHttpExtensions.HttpTaskActivityName, pollRequest);
        }

        return response;
    }

    /// <summary>
    /// Makes a durable HTTP call to the specified URI.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="uri">The target URI.</param>
    /// <param name="content">Optional request body content.</param>
    /// <param name="retryOptions">Optional retry options.</param>
    /// <param name="asynchronousPatternEnabled">Whether to enable automatic HTTP 202 polling.</param>
    /// <returns>The HTTP response.</returns>
    public static Task<DurableHttpResponse> CallHttpAsync(
        this TaskOrchestrationContext context,
        HttpMethod method,
        Uri uri,
        string? content = null,
        HttpRetryOptions? retryOptions = null,
        bool asynchronousPatternEnabled = false)
    {
        var request = new DurableHttpRequest(method, uri)
        {
            Content = content,
            HttpRetryOptions = retryOptions,
            AsynchronousPatternEnabled = asynchronousPatternEnabled,
        };

        return context.CallHttpAsync(request);
    }

    /// <summary>
    /// Makes a durable HTTP call to the specified URI with full configuration options.
    /// </summary>
    /// <param name="context">The orchestration context.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="uri">The target URI.</param>
    /// <param name="content">Optional request body content.</param>
    /// <param name="retryOptions">Optional retry options.</param>
    /// <param name="asynchronousPatternEnabled">Whether to enable automatic HTTP 202 polling.</param>
    /// <param name="tokenSource">Optional token source for authentication (not supported in standalone mode).</param>
    /// <param name="timeout">Optional request timeout.</param>
    /// <returns>The HTTP response.</returns>
    public static Task<DurableHttpResponse> CallHttpAsync(
        this TaskOrchestrationContext context,
        HttpMethod method,
        Uri uri,
        string? content = null,
        HttpRetryOptions? retryOptions = null,
        bool asynchronousPatternEnabled = false,
        TokenSource? tokenSource = null,
        TimeSpan? timeout = null)
    {
        var request = new DurableHttpRequest(method, uri)
        {
            Content = content,
            HttpRetryOptions = retryOptions,
            AsynchronousPatternEnabled = asynchronousPatternEnabled,
            TokenSource = tokenSource,
            Timeout = timeout,
        };

        return context.CallHttpAsync(request);
    }
}
