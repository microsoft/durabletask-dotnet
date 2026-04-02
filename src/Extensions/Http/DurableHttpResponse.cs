// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Serialization;
using Microsoft.DurableTask.Http.Converters;

namespace Microsoft.DurableTask.Http;

/// <summary>
/// Represents an HTTP response returned by a durable HTTP call made via
/// <see cref="TaskOrchestrationContextHttpExtensions.CallHttpAsync(TaskOrchestrationContext, DurableHttpRequest)"/>.
/// </summary>
/// <remarks>
/// The response data is durably persisted in the orchestration history.
/// This type is wire-compatible with the Azure Functions Durable Task extension's DurableHttpResponse.
/// </remarks>
public class DurableHttpResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DurableHttpResponse"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    public DurableHttpResponse(HttpStatusCode statusCode)
    {
        this.StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the HTTP status code.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets or sets the HTTP response headers.
    /// </summary>
    [JsonPropertyName("headers")]
    [JsonConverter(typeof(HttpHeadersConverter))]
    public IDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the body content of the response.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
