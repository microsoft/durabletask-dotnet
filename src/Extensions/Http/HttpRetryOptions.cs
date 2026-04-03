// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Serialization;

namespace Microsoft.DurableTask.Http;

/// <summary>
/// Defines retry policies for durable HTTP requests.
/// </summary>
public class HttpRetryOptions
{
    static readonly TimeSpan DefaultMaxRetryInterval = TimeSpan.FromDays(6);

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpRetryOptions"/> class.
    /// </summary>
    /// <param name="statusCodesToRetry">The status codes to retry on, or null to retry all 4xx/5xx.</param>
    public HttpRetryOptions(IList<HttpStatusCode>? statusCodesToRetry = null)
    {
        this.StatusCodesToRetry = statusCodesToRetry ?? new List<HttpStatusCode>();
    }

    /// <summary>
    /// Gets or sets the first retry interval.
    /// </summary>
    [JsonPropertyName("FirstRetryInterval")]
    public TimeSpan FirstRetryInterval { get; set; }

    /// <summary>
    /// Gets or sets the max retry interval. Defaults to 6 days.
    /// </summary>
    [JsonPropertyName("MaxRetryInterval")]
    public TimeSpan MaxRetryInterval { get; set; } = DefaultMaxRetryInterval;

    /// <summary>
    /// Gets or sets the backoff coefficient. Defaults to 1.
    /// </summary>
    [JsonPropertyName("BackoffCoefficient")]
    public double BackoffCoefficient { get; set; } = 1;

    /// <summary>
    /// Gets or sets the timeout for retries. Defaults to <see cref="TimeSpan.MaxValue"/>.
    /// </summary>
    [JsonPropertyName("RetryTimeout")]
    public TimeSpan RetryTimeout { get; set; } = TimeSpan.MaxValue;

    /// <summary>
    /// Gets or sets the max number of attempts.
    /// </summary>
    [JsonPropertyName("MaxNumberOfAttempts")]
    public int MaxNumberOfAttempts { get; set; }

    /// <summary>
    /// Gets the list of status codes to retry on. If empty, all 4xx and 5xx are retried.
    /// </summary>
    [JsonPropertyName("StatusCodesToRetry")]
    public IList<HttpStatusCode> StatusCodesToRetry { get; }
}
