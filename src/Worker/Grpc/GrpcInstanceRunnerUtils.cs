// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Utility methods for the <see cref="GrpcOrchestrationRunner"/> and <see cref="GrpcEntityRunner"/> classes.
/// </summary>
static class GrpcInstanceRunnerUtils
{
    /// <summary>
    /// Parses request properties to determine extended session settings and initializes the extended sessions cache if
    /// the settings are properly enabled.
    /// </summary>
    /// <remarks>
    /// If any request property is missing or invalid (i.e. the key is misspelled or the value is of the wrong type),
    /// extended sessions are not enabled and default values are assigned to the returns.
    /// </remarks>
    /// <param name="properties">
    /// A dictionary containing request properties used to configure extended session behavior.
    /// </param>
    /// <param name="extendedSessionsCache">The extended sessions cache manager.</param>
    /// <param name="extendedSessionIdleTimeoutInSeconds">
    /// When the method returns, contains the idle timeout value for extended sessions, in seconds. Cache entries that
    /// have not been accessed in this timeframe are evicted from <paramref name="extendedSessionsCache"/>.
    /// Set to zero if extended sessions are not enabled.
    /// </param>
    /// <param name="isExtendedSession">When the method returns, indicates whether this request is from within an extended session.</param>
    /// <param name="stateIncluded">When the method returns, indicates whether instance state is included in the request.</param>
    /// <param name="extendedSessions">When the method returns, contains the extended sessions cache initialized from
    /// <paramref name="extendedSessionsCache"/> if <paramref name="isExtendedSession"/> and <paramref name="extendedSessionIdleTimeoutInSeconds"/>
    /// are correctly specified in the <paramref name="properties"/>; otherwise, null.
    /// </param>
    internal static void ParseRequestPropertiesAndInitializeCache(
        Dictionary<string, object?> properties,
        ExtendedSessionsCache? extendedSessionsCache,
        out double extendedSessionIdleTimeoutInSeconds,
        out bool isExtendedSession,
        out bool stateIncluded,
        out MemoryCache? extendedSessions)
    {
        // If any of the request parameters are malformed, we assume the default - extended sessions are not enabled and the instance state is attached
        extendedSessions = null;
        stateIncluded = true;
        isExtendedSession = false;
        extendedSessionIdleTimeoutInSeconds = 0;

        // Only attempt to initialize the extended sessions cache if all the parameters are correctly specified
        if (properties.TryGetValue("ExtendedSessionIdleTimeoutInSeconds", out object? extendedSessionIdleTimeoutObj)
            && extendedSessionIdleTimeoutObj is double extendedSessionIdleTimeout
            && extendedSessionIdleTimeout > 0
            && properties.TryGetValue("IsExtendedSession", out object? extendedSessionObj)
            && extendedSessionObj is bool extendedSession)
        {
            extendedSessionIdleTimeoutInSeconds = extendedSessionIdleTimeout;
            isExtendedSession = extendedSession;

            try
            {
                extendedSessions = extendedSessionsCache?.GetOrInitializeCache(extendedSessionIdleTimeoutInSeconds);
            }
            catch (ObjectDisposedException)
            {
                // The extended sessions cache has already been disposed -- e.g. a request raced with the
                // final stage of a graceful worker shutdown. This is not a caller error: fall back to the
                // same "no cache available" behavior already used when extendedSessionsCache is null to
                // begin with (extendedSessions stays null; isExtendedSession is left as parsed above), so
                // the caller degrades to requesting full history instead of failing the call outright.
                // Do not widen this catch beyond ObjectDisposedException -- any other failure here is
                // unexpected and should propagate.
                extendedSessions = null;
            }
        }

        if (properties.TryGetValue("IncludeState", out object? includeStateObj)
            && includeStateObj is bool includeState)
        {
            stateIncluded = includeState;
        }
    }
}
