// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Http;

/// <summary>
/// Built-in activity that executes HTTP requests for the standalone Durable Task SDK.
/// This enables <c>CallHttpAsync</c> to work without the Azure Functions host.
/// </summary>
internal sealed class BuiltInHttpActivity : TaskActivity<DurableHttpRequest, DurableHttpResponse>
{
    readonly HttpClient httpClient;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltInHttpActivity"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="logger">The logger.</param>
    public BuiltInHttpActivity(HttpClient httpClient, ILogger logger)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public override async Task<DurableHttpResponse> RunAsync(
        TaskActivityContext context, DurableHttpRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.TokenSource != null)
        {
            throw new NotSupportedException(
                "TokenSource-based authentication is not supported in standalone mode. " +
                "Pass authentication tokens directly via the request Headers dictionary instead.");
        }

        this.logger.LogInformation(
            "Executing built-in HTTP activity: {Method} {Uri}",
            request.Method,
            request.Uri);

        HttpResponseMessage response = await this.ExecuteWithRetryAsync(request);

        string? body = response.Content != null
            ? await response.Content.ReadAsStringAsync()
            : null;

        IDictionary<string, string>? responseHeaders = MapResponseHeaders(response);

        this.logger.LogInformation(
            "Built-in HTTP activity completed: {Method} {Uri} → {StatusCode}",
            request.Method,
            request.Uri,
            (int)response.StatusCode);

        return new DurableHttpResponse(response.StatusCode)
        {
            Headers = responseHeaders,
            Content = body,
        };
    }

    async Task<HttpResponseMessage> ExecuteWithRetryAsync(DurableHttpRequest request)
    {
        HttpRetryOptions? retryOptions = request.HttpRetryOptions;
        int maxAttempts = retryOptions?.MaxNumberOfAttempts ?? 1;
        if (maxAttempts < 1)
        {
            maxAttempts = 1;
        }

        TimeSpan delay = retryOptions?.FirstRetryInterval ?? TimeSpan.Zero;
        DateTime deadline = retryOptions != null && retryOptions.RetryTimeout < TimeSpan.MaxValue
            ? DateTime.UtcNow + retryOptions.RetryTimeout
            : DateTime.MaxValue;

        HttpResponseMessage? lastResponse = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using HttpRequestMessage httpRequest = BuildHttpRequest(request);

            using var cts = new CancellationTokenSource();
            if (request.Timeout.HasValue)
            {
                cts.CancelAfter(request.Timeout.Value);
            }

            lastResponse?.Dispose();
            lastResponse = await this.httpClient.SendAsync(httpRequest, cts.Token);

            // Check if we should retry
            bool isLastAttempt = attempt >= maxAttempts || DateTime.UtcNow >= deadline;
            if (isLastAttempt || !IsRetryableStatus(lastResponse.StatusCode, retryOptions))
            {
                return lastResponse;
            }

            this.logger.LogWarning(
                "HTTP request to {Uri} returned {StatusCode}, retrying (attempt {Attempt}/{MaxAttempts})",
                request.Uri,
                (int)lastResponse.StatusCode,
                attempt,
                maxAttempts);

            lastResponse.Dispose();
            lastResponse = null;

            await Task.Delay(delay);

            // Calculate next delay with exponential backoff
            double coefficient = retryOptions?.BackoffCoefficient ?? 1;
            delay = TimeSpan.FromTicks((long)(delay.Ticks * coefficient));

            TimeSpan maxInterval = retryOptions?.MaxRetryInterval ?? TimeSpan.FromDays(6);
            if (delay > maxInterval)
            {
                delay = maxInterval;
            }
        }

        // Should not reach here, but return last response as a safety net
        return lastResponse!;
    }

    static HttpRequestMessage BuildHttpRequest(DurableHttpRequest request)
    {
        var httpRequest = new HttpRequestMessage(request.Method, request.Uri);

        if (request.Content != null)
        {
            httpRequest.Content = new StringContent(request.Content, Encoding.UTF8, "application/json");
        }

        if (request.Headers != null)
        {
            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                // Try request headers first, then content headers
                if (!httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    httpRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        return httpRequest;
    }

    static bool IsRetryableStatus(HttpStatusCode statusCode, HttpRetryOptions? retryOptions)
    {
        if (retryOptions == null)
        {
            return false;
        }

        if (retryOptions.StatusCodesToRetry.Count > 0)
        {
            return retryOptions.StatusCodesToRetry.Contains(statusCode);
        }

        // Default: retry all 4xx and 5xx
        int code = (int)statusCode;
        return code >= 400;
    }

    static IDictionary<string, string>? MapResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Content?.Headers != null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        return headers.Count > 0 ? headers : null;
    }
}
