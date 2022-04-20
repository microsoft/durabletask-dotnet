// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.DurableTask.Grpc;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Tests;

/// <summary>
/// Base class for integration tests that use a in-process sidecar for executing orchestrations.
/// </summary>
public class IntegrationTestBase : IClassFixture<GrpcSidecarFixture>, IDisposable
{
    readonly CancellationTokenSource testTimeoutSource = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));
    readonly TestLogProvider logProvider;
    readonly ILoggerFactory loggerFactory;

    // Documentation on xunit test fixtures: https://xunit.net/docs/shared-context
    readonly GrpcSidecarFixture sidecarFixture;

    public IntegrationTestBase(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
    {
        this.logProvider = new(output);
        this.loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(this.logProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        this.sidecarFixture = sidecarFixture;
    }

    /// <summary>
    /// Gets a <see cref="CancellationToken"/> that triggers after a default test timeout period.
    /// The actual timeout value is increased if a debugger is attached to the test process.
    /// </summary>
    public CancellationToken TimeoutToken => this.testTimeoutSource.Token;

    void IDisposable.Dispose()
    {
        this.testTimeoutSource.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a <see cref="DurableTaskGrpcWorker"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    protected DurableTaskGrpcWorker.Builder CreateWorkerBuilder()
    {
        return this.sidecarFixture.GetWorkerBuilder().UseLoggerFactory(this.loggerFactory);
    }

    /// <summary>
    /// Creates a <see cref="DurableTaskGrpcClient"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    protected DurableTaskClient CreateDurableTaskClient()
    {
        return this.sidecarFixture.GetClientBuilder().UseLoggerFactory(this.loggerFactory).Build();
    }

    protected IReadOnlyCollection<LogEntry> GetLogs()
    {
        // NOTE: Renaming the log category is a breaking change!
        const string ExpectedCategoryName = "Microsoft.DurableTask";
        bool foundCategory = this.logProvider.TryGetLogs(ExpectedCategoryName, out IReadOnlyCollection<LogEntry> logs);
        Assert.True(foundCategory);
        return logs;
    }
}
