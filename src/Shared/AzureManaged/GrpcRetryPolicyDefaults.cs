// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client.Configuration;

namespace Dapr.DurableTask;

/// <summary>
/// Provides default retry policy configurations for gRPC client connections.
/// </summary>
sealed class GrpcRetryPolicyDefaults
{
    /// <summary>
    /// Default maximum number of retry attempts.
    /// </summary>
    public const int DefaultMaxAttempts = 10;

    /// <summary>
    /// Default initial backoff in milliseconds.
    /// </summary>
    public const int DefaultInitialBackoffMs = 50;

    /// <summary>
    /// Default maximum backoff in milliseconds.
    /// </summary>
    public const int DefaultMaxBackoffMs = 250;

    /// <summary>
    /// Default backoff multiplier for exponential backoff.
    /// </summary>
    public const double DefaultBackoffMultiplier = 2;

    /// <summary>
    /// The default service configuration that includes the method configuration.
    /// </summary>
    /// <remarks>
    /// This can be applied to a gRPC channel to enable automatic retries for all methods.
    /// </remarks>
    public static readonly ServiceConfig DefaultServiceConfig;

    /// <summary>
    /// The default retry policy for gRPC operations.
    /// </summary>
    /// <remarks>
    /// - Retries only for Unavailable status codes (typically connection issues).
    /// </remarks>
    static readonly Grpc.Net.Client.Configuration.RetryPolicy Default;

    /// <summary>
    /// The default method configuration that applies the retry policy to all methods.
    /// </summary>
    /// <remarks>
    /// Uses MethodName.Default to apply the retry policy to all gRPC methods.
    /// </remarks>
    static readonly MethodConfig DefaultMethodConfig;

    static GrpcRetryPolicyDefaults()
    {
        Default = new Grpc.Net.Client.Configuration.RetryPolicy
        {
            MaxAttempts = DefaultMaxAttempts,
            InitialBackoff = TimeSpan.FromMilliseconds(DefaultInitialBackoffMs),
            MaxBackoff = TimeSpan.FromMilliseconds(DefaultMaxBackoffMs),
            BackoffMultiplier = DefaultBackoffMultiplier,
            RetryableStatusCodes = { StatusCode.Unavailable },
        };

        DefaultMethodConfig = new MethodConfig
        {
            Names = { MethodName.Default },
            RetryPolicy = Default,
        };

        DefaultServiceConfig = new ServiceConfig
        {
            MethodConfigs = { DefaultMethodConfig },
        };
    }
}
