// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;

namespace Microsoft.DurableTask.Samples.Serverless.RemoteWorker;

/// <summary>
/// A no-op TokenCredential for sandbox-hosted workers running against a backend that has
/// authentication disabled. The DTS backend deployment in this POC sets
/// <c>--ClientAuth:DisableAuthentication=true</c>, so any token (or none) is accepted on the
/// gRPC calls. Inside an ADC sandbox there is no managed identity available and no
/// <c>az login</c> session, so <see cref="Azure.Identity.DefaultAzureCredential"/> would crash
/// during startup. Setting <c>DTS_NO_AUTH=true</c> in the sandbox env causes the SDK to use
/// this credential instead.
/// </summary>
internal sealed class NoAuthCredential : TokenCredential
{
    static readonly AccessToken FakeToken = new("dts-no-auth", DateTimeOffset.UtcNow.AddYears(1));

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => FakeToken;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => ValueTask.FromResult(FakeToken);
}
