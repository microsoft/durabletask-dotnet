// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Tests.Logging;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Base class for integration tests that use a in-process sidecar for executing orchestrations.
/// </summary>
public class IntegrationTestBase : IClassFixture<GrpcSidecarFixture>, IDisposable
{
    readonly CancellationTokenSource testTimeoutSource
        = new(Debugger.IsAttached ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(10));

    readonly TestLogProvider logProvider;

    // Documentation on xunit test fixtures: https://xunit.net/docs/shared-context
    readonly GrpcSidecarFixture sidecarFixture;

    public IntegrationTestBase(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
    {
        this.logProvider = new(output);
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

    protected async Task<HostTestLifetime> StartWorkerAsync(Action<IDurableTaskWorkerBuilder> configure)
    {
        IHost host = this.CreateHostBuilder(configure).Build();
        await host.StartAsync(this.TimeoutToken);
        return new HostTestLifetime(host, this.TimeoutToken);
    }

    /// <summary>
    /// Creates a <see cref="IHostBuilder"/> configured to output logs to xunit logging infrastructure.
    /// </summary>
    /// <param name="configure">Configures the durable task builder.</param>
    protected IHostBuilder CreateHostBuilder(Action<IDurableTaskWorkerBuilder> configure)
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

                services.AddDurableTaskClient(b =>
                {
                    b.UseGrpc(this.sidecarFixture.Channel);
                    b.RegisterDirectly();
                });
            });
    }

    protected IReadOnlyCollection<LogEntry> GetLogs()
    {
        // NOTE: Renaming the log category is a breaking change!
        const string ExpectedCategoryName = "Microsoft.DurableTask";
        bool foundCategory = this.logProvider.TryGetLogs(ExpectedCategoryName, out IReadOnlyCollection<LogEntry> logs);
        Assert.True(foundCategory);
        return logs;
    }

    protected struct HostTestLifetime : IAsyncDisposable
    {
        readonly IHost host;
        readonly CancellationToken cancellation;

        public HostTestLifetime(IHost host, CancellationToken cancellation)
        {
            this.host = host;
            this.cancellation = cancellation;
            this.Client = host.Services.GetRequiredService<DurableTaskClient>();
        }

        public DurableTaskClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.host.StopAsync(this.cancellation);
            }
            finally
            {
                this.host.Dispose();
            }
        }
    }
}
