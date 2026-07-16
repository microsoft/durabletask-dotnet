// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

/// <summary>
/// Tests for the non-secret external payload store base URI that the worker reports on the work-item handshake.
/// </summary>
public class ExternalPayloadStoreUriTests
{
    // Well-known Azurite development storage account key. Used here only to prove it is never leaked into the base URI.
    const string DevelopmentAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Fact]
    public void ExternalPayloadStoreBaseUri_EmulatorConnectionString_ReturnsAccountBaseUriWithoutContainerOrSas()
    {
        // Arrange
        LargePayloadStorageOptions options = new("UseDevelopmentStorage=true")
        {
            ContainerName = "my-payloads",
        };
        BlobPayloadStore store = new(options);

        // Act
        Uri? baseUri = store.ExternalPayloadStoreBaseUri;

        // Assert
        baseUri.Should().NotBeNull();
        baseUri!.AbsoluteUri.TrimEnd('/').Should().Be("http://127.0.0.1:10000/devstoreaccount1");
        baseUri.Query.Should().BeEmpty();
        baseUri.AbsoluteUri.Should().NotContain("my-payloads");
    }

    [Fact]
    public void ExternalPayloadStoreBaseUri_ConnectionStringWithAccountKey_ReturnsAccountBaseUriAndNeverLeaksKey()
    {
        // Arrange
        LargePayloadStorageOptions options = new(
            $"DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey={DevelopmentAccountKey};EndpointSuffix=core.windows.net")
        {
            ContainerName = "my-payloads",
        };
        BlobPayloadStore store = new(options);

        // Act
        Uri? baseUri = store.ExternalPayloadStoreBaseUri;

        // Assert
        baseUri.Should().Be(new Uri("https://myaccount.blob.core.windows.net/"));
        baseUri!.Query.Should().BeEmpty();
        baseUri.AbsoluteUri.Should().NotContain(DevelopmentAccountKey);
        baseUri.AbsoluteUri.Should().NotContain("my-payloads");
    }

    [Fact]
    public void ExternalPayloadStoreBaseUri_AccountUriAndCredential_ReturnsAccountBaseUriWithoutContainerOrSas()
    {
        // Arrange
        LargePayloadStorageOptions options = new(
            new Uri("https://myaccount.blob.core.windows.net"),
            new FakeTokenCredential())
        {
            ContainerName = "my-payloads",
        };
        BlobPayloadStore store = new(options);

        // Act
        Uri? baseUri = store.ExternalPayloadStoreBaseUri;

        // Assert
        baseUri.Should().Be(new Uri("https://myaccount.blob.core.windows.net/"));
        baseUri!.Query.Should().BeEmpty();
        baseUri.AbsoluteUri.Should().NotContain("my-payloads");
    }

    [Fact]
    public void UseExternalizedPayloads_SetsExternalPayloadStoreUriFromStore()
    {
        // Arrange
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new(null, services);
        builder.UseGrpc(GrpcChannel.ForAddress("http://localhost:9001"));
        builder.UseExternalizedPayloads(opt =>
        {
            opt.ContainerName = "my-payloads";
            opt.ConnectionString = "UseDevelopmentStorage=true";
        });

        // Act
        IServiceProvider provider = services.BuildServiceProvider();
        GrpcDurableTaskWorkerOptions options = provider.GetOptions<GrpcDurableTaskWorkerOptions>();

        // Assert
        options.ExternalPayloadStoreUri.Should().NotBeNullOrEmpty();
        options.ExternalPayloadStoreUri!.TrimEnd('/').Should().Be("http://127.0.0.1:10000/devstoreaccount1");
    }

    sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("fake-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(this.GetToken(requestContext, cancellationToken));
    }
}
