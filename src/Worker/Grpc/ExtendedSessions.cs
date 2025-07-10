// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Microsoft.DurableTask.Worker.Grpc;

public class ExtendedSessions
{
    readonly Dictionary<string, ExtendedSessionState> extendedSessions = [];
    readonly object extendedSessionsLock = new object();
    readonly int extendedSessionIdleTimeoutInSeconds;

    internal void Add(string instanceId, ExtendedSessionState sessionState)
    {
        lock (this.extendedSessionsLock)
        {
            this.extendedSessions[instanceId] = sessionState;
        }
    }

    internal void Remove(string instanceId)
    {
        lock (this.extendedSessionsLock)
        {
            this.extendedSessions.Remove(instanceId);
        }
    }

    internal bool TryGetValue(string instanceId, out ExtendedSessionState? sessionState)
    {
        lock (this.extendedSessionsLock)
        {
            bool success = this.extendedSessions.TryGetValue(instanceId, out sessionState);

            return success;
        }
    }

    void Purge()
    {
    }
}
