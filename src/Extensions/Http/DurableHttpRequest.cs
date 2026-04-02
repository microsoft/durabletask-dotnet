// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http;
using System.Text.Json.Serialization;
using Microsoft.DurableTask.Http.Converters;

namespace Microsoft.DurableTask.Http;

/// <summary>
/// Represents an HTTP request that can be made by an orchestrator function using
/// <see cref="TaskOrchestrationContextHttpExtensions.CallHttpAsync(TaskOrchestrationContext, DurableHttpRequest)"/>.
/// </summary>
/// <remarks>
/// The request is serialized and persisted in the orchestration history, making it safe for replay.
/// This type is wire-compatible with the Azure Functions Durable Task extension's DurableHttpRequest.
/// </remarks>
public class DurableHttpRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableHttpRequest"/> class.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="uri">The target URI.</param>
    public DurableHttpRequest(HttpMethod method, Uri uri)
    {
        this.Method = method ?? throw new ArgumentNullException(nameof(method));
        this.Uri = uri ?? throw new ArgumentNullException(nameof(uri));
    }

    /// <summary>
    /// Gets the HTTP method for the request.
    /// </summary>
    [JsonPropertyName("method")]
    [JsonConverter(typeof(HttpMethodConverter))]
    public HttpMethod Method { get; }

    /// <summary>
    /// Gets the target URI for the request.
    /// </summary>
    [JsonPropertyName("uri")]
    public Uri Uri { get; }

    /// <summary>
    /// Gets or sets the HTTP headers for the request.
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonConverter(typeof(HttpHeadersConverter))]
    public IDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the body content of the request.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Gets or sets the token source for authentication.
    /// </summary>
    /// <remarks>
    /// Token acquisition is not supported in standalone mode. If set, an exception will be thrown at execution time.
    /// Pass authentication tokens directly via the <see cref="Headers"/> dictionary instead.
    /// </remarks>
    [JsonPropertyName("tokenSource")]
    [JsonConverter(typeof(TokenSourceConverter))]
    public TokenSource? TokenSource { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the asynchronous HTTP 202 polling pattern is enabled.
    /// </summary>
    /// <remarks>
    /// When enabled and the target returns HTTP 202 with a Location header, the framework will
    /// automatically poll until a non-202 response is received.
    /// </remarks>
    [JsonPropertyName("asynchronousPatternEnabled")]
    public bool AsynchronousPatternEnabled { get; set; }

    /// <summary>
    /// Gets or sets the retry options for the HTTP request.
    /// </summary>
    [JsonPropertyName("retryOptions")]
    public HttpRetryOptions? HttpRetryOptions { get; set; }

    /// <summary>
    /// Gets or sets the total timeout for the HTTP request and any asynchronous polling.
    /// </summary>
    [JsonPropertyName("timeout")]
    public TimeSpan? Timeout { get; set; }
}
