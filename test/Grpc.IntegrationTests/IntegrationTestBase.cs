// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Grpc;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Base class for integration tests that use a in-process sidecar for executing orchestrations.
/// </summary>
public class IntegrationTestBase : IClassFixture<GrpcSidecarFixture>, IDisposable
{
    readonly CancellationTokenSource testTimeoutSource
        = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));

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

    protected async Task<AsyncDisposable> StartWorkerAsync(Action<IDurableTaskBuilder> configure)
    {
        IHost host = this.CreateWorkerBuilder(configure).Build();
        await host.StartAsync(this.TimeoutToken);
        return new AsyncDisposable(async () =>
        {
            try
            {
                await host.StopAsync(this.TimeoutToken);
            }
            catch (OperationCanceledException)
            {
            }

            host.Dispose();
        });
    }

    /// <summary>
    /// Creates a <see cref="IHostBuilder"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    /// <param name="configure">Configures the durable task builder.</param>
    protected IHostBuilder CreateWorkerBuilder(Action<IDurableTaskBuilder> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(b =>
            {
                b.ClearProviders();
                b.AddProvider(this.logProvider);
                b.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddDurableTaskWorker(b =>
                {
                    b.UseGrpc(this.sidecarFixture.Channel);
                    configure(b);
                });
            });
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
