using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using FluentAssertions;
using Microsoft.DurableTask.Extensions.Azure;
using Moq;
using Xunit;

namespace Microsoft.DurableTask.Tests.Extensions.Azure;

public class AccessTokenCacheTests
{
    private readonly Mock<TokenCredential> mockCredential;
    private readonly TokenRequestContext tokenRequestContext;
    private readonly TimeSpan margin;
    private readonly CancellationToken cancellationToken;

    public AccessTokenCacheTests()
    {
        mockCredential = new Mock<TokenCredential>();
        tokenRequestContext = new TokenRequestContext(new[] { "https://durabletask.azure.com/.default" });
        margin = TimeSpan.FromMinutes(5);
        cancellationToken = CancellationToken.None;
    }

    [Fact]
    public async Task GetTokenAsync_WhenCalled_ShouldReturnToken()
    {
        // Arrange
        var expectedToken = new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        mockCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedToken);
        var cache = new AccessTokenCache(mockCredential.Object, tokenRequestContext, margin);

        // Act
        var token = await cache.GetTokenAsync(cancellationToken);

        // Assert
        token.Should().Be(expectedToken);
        mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenExpired_ShouldRequestNewToken()
    {
        // Arrange
        var expiredToken = new AccessToken("expired-token", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newToken = new AccessToken("new-token", DateTimeOffset.UtcNow.AddHours(1));
        var cache = new AccessTokenCache(mockCredential.Object, tokenRequestContext, margin);

        mockCredential.SetupSequence(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken)
            .ReturnsAsync(newToken);

        // Act
        var firstToken = await cache.GetTokenAsync(cancellationToken);
        var secondToken = await cache.GetTokenAsync(cancellationToken);

        // Assert
        firstToken.Should().Be(expiredToken);
        secondToken.Should().Be(newToken);
        mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenValid_ShouldReturnCachedToken()
    {
        // Arrange
        var validToken = new AccessToken("valid-token", DateTimeOffset.UtcNow.AddHours(1));
        mockCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validToken);
        var cache = new AccessTokenCache(mockCredential.Object, tokenRequestContext, margin);

        // Act
        var firstToken = await cache.GetTokenAsync(cancellationToken);
        var secondToken = await cache.GetTokenAsync(cancellationToken);

        // Assert
        firstToken.Should().Be(validToken);
        secondToken.Should().Be(validToken);
        mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Constructor_WithNullCredential_ShouldThrowNullReferenceException()
    {
        // Arrange
        var cache = new AccessTokenCache(null!, tokenRequestContext, margin);

        // Act & Assert
        // TODO: The constructor should validate its parameters and throw ArgumentNullException,
        // but currently it allows null parameters and throws NullReferenceException when used.
        var action = () => cache.GetTokenAsync(cancellationToken);
        await action.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task GetTokenAsync_WhenTokenNearExpiry_ShouldRequestNewToken()
    {
        // Arrange
        var expiryTime = DateTimeOffset.UtcNow.AddMinutes(10);
        var nearExpiryToken = new AccessToken("near-expiry-token", expiryTime);
        var newToken = new AccessToken("new-token", expiryTime.AddHours(1));
        var cache = new AccessTokenCache(mockCredential.Object, tokenRequestContext, TimeSpan.FromMinutes(15));

        mockCredential.SetupSequence(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nearExpiryToken)
            .ReturnsAsync(newToken);

        // Act
        var firstToken = await cache.GetTokenAsync(cancellationToken);
        var secondToken = await cache.GetTokenAsync(cancellationToken);

        // Assert
        firstToken.Should().Be(nearExpiryToken);
        secondToken.Should().Be(newToken);
        mockCredential.Verify(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
