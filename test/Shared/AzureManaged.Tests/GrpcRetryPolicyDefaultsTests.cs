// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Xunit;

namespace Microsoft.DurableTask.Tests;

public class GrpcRetryPolicyDefaultsTests
{
    [Fact]
    public void DefaultServiceConfig_ShouldHaveExpectedConfiguration()
    {
        ServiceConfig serviceConfig = GrpcRetryPolicyDefaults.DefaultServiceConfig;

        serviceConfig.Should().NotBeNull();
        serviceConfig.MethodConfigs.Should().HaveCount(1);

        MethodConfig methodConfig = serviceConfig.MethodConfigs[0];
        methodConfig.Names.Should().Contain(MethodName.Default);
        methodConfig.RetryPolicy.Should().NotBeNull();

        RetryPolicy? retryPolicy = methodConfig.RetryPolicy;
        retryPolicy.Should().NotBeNull();
        _ = retryPolicy!.MaxAttempts.Should().Be(GrpcRetryPolicyDefaults.DefaultMaxAttempts);
        retryPolicy.InitialBackoff.Should().Be(TimeSpan.FromMilliseconds(GrpcRetryPolicyDefaults.DefaultInitialBackoffMs));
        retryPolicy.MaxBackoff.Should().Be(TimeSpan.FromMilliseconds(GrpcRetryPolicyDefaults.DefaultMaxBackoffMs));
        retryPolicy.BackoffMultiplier.Should().Be(GrpcRetryPolicyDefaults.DefaultBackoffMultiplier);
        retryPolicy.RetryableStatusCodes.Should().ContainSingle()
            .Which.Should().Be(StatusCode.Unavailable);
    }
} 