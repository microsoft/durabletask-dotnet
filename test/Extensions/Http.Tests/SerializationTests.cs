// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.DurableTask.Http;
using Xunit;

namespace Microsoft.DurableTask.Tests.Http;

public class SerializationTests
{
    static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void DurableHttpRequest_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var request = new DurableHttpRequest(HttpMethod.Post, new Uri("https://example.com/api"))
        {
            Content = "{\"key\":\"value\"}",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = "Bearer token123",
            },
            AsynchronousPatternEnabled = true,
            Timeout = TimeSpan.FromMinutes(5),
            HttpRetryOptions = new HttpRetryOptions
            {
                MaxNumberOfAttempts = 3,
                FirstRetryInterval = TimeSpan.FromSeconds(5),
                BackoffCoefficient = 2.0,
            },
        };

        // Act
        string json = JsonSerializer.Serialize(request, Options);
        DurableHttpRequest? deserialized = JsonSerializer.Deserialize<DurableHttpRequest>(json, Options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Method.Should().Be(HttpMethod.Post);
        deserialized.Uri.Should().Be(new Uri("https://example.com/api"));
        deserialized.Content.Should().Be("{\"key\":\"value\"}");
        deserialized.Headers.Should().HaveCount(2);
        deserialized.AsynchronousPatternEnabled.Should().BeTrue();
        deserialized.Timeout.Should().Be(TimeSpan.FromMinutes(5));
        deserialized.HttpRetryOptions.Should().NotBeNull();
        deserialized.HttpRetryOptions!.MaxNumberOfAttempts.Should().Be(3);
    }

    [Fact]
    public void DurableHttpResponse_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var response = new DurableHttpResponse(HttpStatusCode.OK)
        {
            Content = "{\"result\":true}",
            Headers = new Dictionary<string, string>
            {
                ["X-Request-Id"] = "abc-123",
            },
        };

        // Act
        string json = JsonSerializer.Serialize(response, Options);
        DurableHttpResponse? deserialized = JsonSerializer.Deserialize<DurableHttpResponse>(json, Options);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.StatusCode.Should().Be(HttpStatusCode.OK);
        deserialized.Content.Should().Be("{\"result\":true}");
        deserialized.Headers.Should().ContainKey("X-Request-Id");
    }

    [Fact]
    public void DurableHttpRequest_MinimalGet_SerializesCorrectly()
    {
        // Arrange
        var request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com"));

        // Act
        string json = JsonSerializer.Serialize(request, Options);

        // Assert
        json.Should().Contain("\"method\":\"GET\"");
        json.Should().Contain("\"uri\":\"https://example.com\"");
    }

    [Fact]
    public void DurableHttpResponse_MinimalResponse_DeserializesCorrectly()
    {
        // Arrange
        string json = "{\"statusCode\":404}";

        // Act
        DurableHttpResponse? response = JsonSerializer.Deserialize<DurableHttpResponse>(json, Options);

        // Assert
        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Should().BeNull();
        response.Headers.Should().BeNull();
    }

    [Fact]
    public void TokenSource_ManagedIdentity_SerializesCorrectly()
    {
        // Arrange
        var request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com"))
        {
            TokenSource = new ManagedIdentityTokenSource("https://management.core.windows.net"),
        };

        // Act
        string json = JsonSerializer.Serialize(request, Options);

        // Assert
        json.Should().Contain("\"kind\":\"AzureManagedIdentity\"");
        json.Should().Contain("\"resource\":\"https://management.core.windows.net/.default\"");
    }

    [Fact]
    public void HttpRetryOptions_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var options = new HttpRetryOptions(
            statusCodesToRetry: new List<HttpStatusCode> { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            FirstRetryInterval = TimeSpan.FromSeconds(1),
            MaxRetryInterval = TimeSpan.FromMinutes(5),
            BackoffCoefficient = 1.5,
            RetryTimeout = TimeSpan.FromMinutes(30),
            MaxNumberOfAttempts = 10,
        };

        // Act
        string json = JsonSerializer.Serialize(options);
        HttpRetryOptions? deserialized = JsonSerializer.Deserialize<HttpRetryOptions>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.FirstRetryInterval.Should().Be(TimeSpan.FromSeconds(1));
        deserialized.MaxRetryInterval.Should().Be(TimeSpan.FromMinutes(5));
        deserialized.BackoffCoefficient.Should().Be(1.5);
        deserialized.MaxNumberOfAttempts.Should().Be(10);
        deserialized.StatusCodesToRetry.Should().HaveCount(2);
    }

    [Fact]
    public void DurableHttpResponse_NullHeaders_DeserializesAsNull()
    {
        // Arrange — explicit null in JSON
        string json = "{\"statusCode\":200,\"headers\":null}";

        // Act
        DurableHttpResponse? response = JsonSerializer.Deserialize<DurableHttpResponse>(json, Options);

        // Assert
        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().BeNull("null JSON should deserialize as null, not empty dictionary");
    }

    [Fact]
    public void Headers_MultiValueArray_JoinsWithComma()
    {
        // Arrange — header value is an array (some wire formats produce this)
        string json = "{\"statusCode\":200,\"headers\":{\"Accept\":[\"text/html\",\"application/json\"]}}";

        // Act
        DurableHttpResponse? response = JsonSerializer.Deserialize<DurableHttpResponse>(json, Options);

        // Assert
        response.Should().NotBeNull();
        response!.Headers.Should().NotBeNull();
        response.Headers!["Accept"].Should().Be("text/html, application/json",
            "multi-value arrays should be joined with ', ' to match HTTP header semantics");
    }
}
