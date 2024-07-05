// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Sidecar.App
{
    static partial class Logs
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Initializing the Durable Task sidecar. Listen address = {address}, backend type = {backendType}.")]
        public static partial void InitializingSidecar(
            this ILogger logger,
            string address,
            string backendType);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Sidecar initialized successfully in {latencyMs}ms.")]
        public static partial void SidecarInitialized(
            this ILogger logger,
            long latencyMs);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Error,
            Message = "Sidecar listen port {port} is in use by another process!")]
        public static partial void SidecarListenPortAlreadyInUse(
            this ILogger logger,
            int port);

        [LoggerMessage(
            EventId = 4,
            Level = LogLevel.Information,
            Message = "The Durable Task sidecar is shutting down.")]
        public static partial void SidecarShuttingDown(this ILogger logger);
    }
}

