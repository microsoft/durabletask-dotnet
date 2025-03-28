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
    public void DefaultConstants_ShouldHaveExpectedValues()
    {
        // Assert
        GrpcRetryPolicyDefaults.DefaultMaxAttempts.Should().Be(10);
        GrpcRetryPolicyDefaults.DefaultInitialBackoffMs.Should().Be(50);
        GrpcRetryPolicyDefaults.DefaultMaxBackoffMs.Should().Be(250);
        GrpcRetryPolicyDefaults.DefaultBackoffMultiplier.Should().Be(2);
    }

    [Fact]
    public void DefaultServiceConfig_ShouldHaveExpectedConfiguration()
    {
        // Arrange & Act
        ServiceConfig serviceConfig = GrpcRetryPolicyDefaults.DefaultServiceConfig;

        // Assert
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