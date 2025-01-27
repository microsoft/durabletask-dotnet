// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;

namespace Microsoft.DurableTask;

/// <summary>
/// Caches and manages refresh for Azure access tokens.
/// </summary>
sealed class AccessTokenCache
{
    readonly TokenCredential credential;
    readonly TokenRequestContext context;
    readonly TimeSpan margin;

    AccessToken? token;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessTokenCache"/> class.
    /// </summary>
    /// <param name="credential">The token credential to use for authentication.</param>
    /// <param name="context">The token request context.</param>
    /// <param name="margin">The time margin to use for token refresh.</param>
    public AccessTokenCache(TokenCredential credential, TokenRequestContext context, TimeSpan margin)
    {
        this.credential = credential;
        this.context = context;
        this.margin = margin;
    }

    /// <summary>
    /// Gets a token, either from cache or by requesting a new one if needed.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An access token.</returns>
    public async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset nowWithMargin = DateTimeOffset.UtcNow.Add(this.margin);

        if (this.token is null
            || this.token.Value.RefreshOn < nowWithMargin
            || this.token.Value.ExpiresOn < nowWithMargin)
        {
            this.token = await this.credential.GetTokenAsync(this.context, cancellationToken);
        }

        return this.token.Value;
    }
}
