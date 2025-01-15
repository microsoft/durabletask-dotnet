// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using FluentAssertions;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Shared.AzureManaged.Tests;

public class AccessTokenCacheTests
{
    readonly Mock<TokenCredential> mockCredential;
    readonly TokenRequestContext tokenRequestContext;
    readonly TimeSpan margin;
    readonly CancellationToken cancellationToken;

    public AccessTokenCacheTests()
    {
        this.mockCredential = new Mock<TokenCredential>();
        this.tokenRequestContext = new TokenRequestContext(new[] { "https://durabletask.azure.com/.default" });
        this.margin = TimeSpan.FromMinutes(5);
        this.cancellationToken = CancellationToken.None;
    }

    [Fact]
    public async Task GetTokenAsync_WhenCalled_ShouldReturnToken()
    {
        // Arrange
        AccessToken expectedToken = new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        this.mockCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToken);
        AccessTokenCache cache = new AccessTokenCache(this.mockCredential.Object, this.tokenRequestContext, this.margin);

        // Act
        AccessToken token = await cache.GetTokenAsync(this.cancellationToken);

        // Assert
        token.Should().Be(expectedToken);
        this.mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenExpired_ShouldRequestNewToken()
    {
        // Arrange
        AccessToken expiredToken = new AccessToken("expired-token", DateTimeOffset.UtcNow.AddMinutes(-5));
        AccessToken newToken = new AccessToken("new-token", DateTimeOffset.UtcNow.AddHours(1));
        AccessTokenCache cache = new AccessTokenCache(this.mockCredential.Object, this.tokenRequestContext, this.margin);

        this.mockCredential.SetupSequence(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken)
            .ReturnsAsync(newToken);

        // Act
        AccessToken firstToken = await cache.GetTokenAsync(this.cancellationToken);
        AccessToken secondToken = await cache.GetTokenAsync(this.cancellationToken);

        // Assert
        firstToken.Should().Be(expiredToken);
        secondToken.Should().Be(newToken);
        this.mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenValid_ShouldReturnCachedToken()
    {
        // Arrange
        AccessToken validToken = new AccessToken("valid-token", DateTimeOffset.UtcNow.AddHours(1));
        this.mockCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validToken);
        AccessTokenCache cache = new AccessTokenCache(this.mockCredential.Object, this.tokenRequestContext, this.margin);

        // Act
        AccessToken firstToken = await cache.GetTokenAsync(this.cancellationToken);
        AccessToken secondToken = await cache.GetTokenAsync(this.cancellationToken);

        // Assert
        firstToken.Should().Be(validToken);
        secondToken.Should().Be(validToken);
        this.mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Constructor_WithNullCredential_ShouldThrowNullReferenceException()
    {
        // Arrange
        AccessTokenCache cache = new AccessTokenCache(null!, this.tokenRequestContext, this.margin);

        // Act & Assert
        // TODO: The constructor should validate its parameters and throw ArgumentNullException,
        // but currently it allows null parameters and throws NullReferenceException when used.
        Func<Task> action = () => cache.GetTokenAsync(this.cancellationToken);
        await action.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenNearExpiry_ShouldRequestNewToken()
    {
        // Arrange
        DateTimeOffset expiryTime = DateTimeOffset.UtcNow.AddMinutes(10);
        AccessToken nearExpiryToken = new AccessToken("near-expiry-token", expiryTime);
        AccessToken newToken = new AccessToken("new-token", expiryTime.AddHours(1));
        AccessTokenCache cache = new AccessTokenCache(this.mockCredential.Object, this.tokenRequestContext, TimeSpan.FromMinutes(15));

        this.mockCredential.SetupSequence(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nearExpiryToken)
            .ReturnsAsync(newToken);

        // Act
        AccessToken firstToken = await cache.GetTokenAsync(this.cancellationToken);
        AccessToken secondToken = await cache.GetTokenAsync(this.cancellationToken);

        // Assert
        firstToken.Should().Be(nearExpiryToken);
        secondToken.Should().Be(newToken);
        this.mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
