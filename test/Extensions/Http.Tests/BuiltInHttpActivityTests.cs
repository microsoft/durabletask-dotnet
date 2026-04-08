// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.DurableTask.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Http.Tests;

public class BuiltInHttpActivityTests
{
    static BuiltInHttpActivity CreateActivity(MockHttpHandler handler)
    {
        HttpClient client = new HttpClient(handler);
        return new BuiltInHttpActivity(client, NullLogger.Instance);
    }

    static Mock<TaskActivityContext> CreateContext()
    {
        Mock<TaskActivityContext> mock = new Mock<TaskActivityContext>();
        mock.Setup(c => c.InstanceId).Returns("test-instance");
        return mock;
    }

    [Fact]
    public async Task RunAsync_SimpleGet_ReturnsOk()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello-world"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"));

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Should().Be("hello-world");
        handler.RequestsSent.Should().HaveCount(1);
        handler.RequestsSent[0].Method.Should().Be(HttpMethod.Get);
        handler.RequestsSent[0].RequestUri.Should().Be(new Uri("https://example.com/api"));
    }

    [Fact]
    public async Task RunAsync_PostWithBodyAndHeaders_SendsCorrectly()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("created"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Post, new Uri("https://example.com/api"))
        {
            Content = "test-body",
            Headers = new Dictionary<string, string>
            {
                ["X-Custom-Header"] = "custom-value",
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        handler.RequestsSent.Should().HaveCount(1);

        HttpRequestMessage sent = handler.RequestsSent[0];
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Headers.Contains("X-Custom-Header").Should().BeTrue();

        string? body = handler.RequestBodies[0];
        body.Should().Contain("test-body");
    }

    [Fact]
    public async Task RunAsync_TokenSourceSet_ThrowsNotSupportedException()
    {
        MockHttpHandler handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK));
        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"))
        {
            TokenSource = new ManagedIdentityTokenSource("https://management.core.windows.net/.default"),
        };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => activity.RunAsync(CreateContext().Object, request));
    }

    [Fact]
    public async Task RunAsync_RetryOnServerError_RetriesAndSucceeds()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("success"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"))
        {
            HttpRetryOptions = new HttpRetryOptions
            {
                MaxNumberOfAttempts = 3,
                FirstRetryInterval = TimeSpan.FromMilliseconds(1),
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Should().Be("success");
        handler.RequestsSent.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_RetryExhausted_ReturnsLastError()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error1") },
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error2") },
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error3") });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"))
        {
            HttpRetryOptions = new HttpRetryOptions
            {
                MaxNumberOfAttempts = 3,
                FirstRetryInterval = TimeSpan.FromMilliseconds(1),
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.RequestsSent.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_RetryWithSpecificStatusCodes_OnlyRetriesMatchingCodes()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"))
        {
            HttpRetryOptions = new HttpRetryOptions
            {
                MaxNumberOfAttempts = 5,
                FirstRetryInterval = TimeSpan.FromMilliseconds(1),
                StatusCodesToRetry = { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable },
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.RequestsSent.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_ResponseHeaders_AreMapped()
    {
        HttpResponseMessage httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("body"),
        };
        httpResponse.Headers.Add("X-Request-Id", "abc123");

        MockHttpHandler handler = new MockHttpHandler(httpResponse);
        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"));

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.Headers.Should().NotBeNull();
        response.Headers.Should().ContainKey("X-Request-Id");
        response.Headers!["X-Request-Id"].Should().Be("abc123");
    }

    [Fact]
    public async Task RunAsync_NullRequest_ThrowsArgumentNull()
    {
        MockHttpHandler handler = new MockHttpHandler(new HttpResponseMessage(HttpStatusCode.OK));
        BuiltInHttpActivity activity = CreateActivity(handler);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => activity.RunAsync(CreateContext().Object, null!));
    }

    [Fact]
    public async Task RunAsync_CustomContentType_HonorsUserHeader()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Post, new Uri("https://example.com/api"))
        {
            Content = "name=test&value=123",
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestsSent.Should().HaveCount(1);

        HttpRequestMessage sent = handler.RequestsSent[0];
        sent.Content.Should().NotBeNull();
        sent.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
    }

    [Fact]
    public async Task RunAsync_NoContentTypeHeader_DefaultsToApplicationJson()
    {
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Post, new Uri("https://example.com/api"))
        {
            Content = "{\"key\":\"value\"}",
        };

        await activity.RunAsync(CreateContext().Object, request);

        HttpRequestMessage sent = handler.RequestsSent[0];
        sent.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task RunAsync_ZeroRetryInterval_UsesMinimumDelay()
    {
        // When FirstRetryInterval is not set (zero), the retry loop should not
        // hammer the server with no delay. Verify it still retries (not stuck).
        MockHttpHandler handler = new MockHttpHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("success"),
            });

        BuiltInHttpActivity activity = CreateActivity(handler);
        DurableHttpRequest request = new DurableHttpRequest(HttpMethod.Get, new Uri("https://example.com/api"))
        {
            HttpRetryOptions = new HttpRetryOptions
            {
                MaxNumberOfAttempts = 2,
                // FirstRetryInterval intentionally not set — should default to 1s floor
            },
        };

        DurableHttpResponse response = await activity.RunAsync(CreateContext().Object, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestsSent.Should().HaveCount(2);
    }

    sealed class MockHttpHandler : HttpMessageHandler
    {
        readonly Queue<HttpResponseMessage> responses;

        public MockHttpHandler(params HttpResponseMessage[] responses)
        {
            this.responses = new Queue<HttpResponseMessage>(responses);
            this.RequestsSent = new List<HttpRequestMessage>();
        }

        public List<HttpRequestMessage> RequestsSent { get; }
        public List<string?> RequestBodies { get; } = new List<string?>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestsSent.Add(request);
            this.RequestBodies.Add(request.Content?.ReadAsStringAsync().Result);

            if (this.responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(this.responses.Dequeue());
        }
    }
}
