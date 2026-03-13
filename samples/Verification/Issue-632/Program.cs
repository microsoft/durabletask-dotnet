// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Verification sample for Issue #632: gRPC channel cache not invalidated on options change
//
// Customer scenario: A long-lived application configures the DTS client and worker via
// UseDurableTaskScheduler. When configuration values change at runtime (e.g., endpoint
// rotation or task hub migration), cached gRPC channels should be invalidated and recreated.
//
// Before fix: Cached gRPC channels were never invalidated when IOptionsMonitor detected
// options changes, causing stale connections to persist until process restart.
//
// After fix: OnChange subscriptions detect runtime options changes, compare cache keys,
// and invalidate/dispose stale channels so fresh channels are created on next resolution.

using Azure.Identity;
using Grpc.Net.Client;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

int passCount = 0;
int failCount = 0;

// === Check 1: Client channel invalidation on endpoint change ===
Console.WriteLine("[Check 1] Client: options change invalidates cached channel...");
{
    ServiceCollection services = new();
    DefaultAzureCredential credential = new();
    string[] mutableEndpoint = ["myendpoint.westus3.durabletask.io"];

    new StubClientBuilder(services).UseDurableTaskScheduler(
        "myendpoint.westus3.durabletask.io", "default", credential);

    // PostConfigure lets us mutate the endpoint at runtime, simulating a config reload.
    services.PostConfigure<DurableTaskSchedulerClientOptions>(Options.DefaultName, o =>
    {
        o.EndpointAddress = mutableEndpoint[0];
    });

    ManualChangeTokenSource<DurableTaskSchedulerClientOptions> changeSource = new(Options.DefaultName);
    services.AddSingleton<IOptionsChangeTokenSource<DurableTaskSchedulerClientOptions>>(changeSource);

    await using ServiceProvider provider = services.BuildServiceProvider();
    IOptionsMonitor<GrpcDurableTaskClientOptions> monitor =
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();

    GrpcChannel initialChannel = monitor.Get(Options.DefaultName).Channel!;

    // Simulate an endpoint change and signal the options system.
    mutableEndpoint[0] = "changed.westus3.durabletask.io";
    changeSource.SignalChange();

    GrpcChannel newChannel = monitor.Get(Options.DefaultName).Channel!;

    if (!ReferenceEquals(initialChannel, newChannel))
    {
        Console.WriteLine("  ✅ PASS: Channel was invalidated after endpoint change");
        passCount++;
    }
    else
    {
        Console.WriteLine("  ❌ FAIL: Channel was NOT invalidated after endpoint change");
        failCount++;
    }
}

// === Check 2: Client channel retention when options unchanged ===
Console.WriteLine("[Check 2] Client: unchanged options retain cached channel...");
{
    ServiceCollection services = new();
    DefaultAzureCredential credential = new();

    new StubClientBuilder(services).UseDurableTaskScheduler(
        "myendpoint.westus3.durabletask.io", "default", credential);

    ManualChangeTokenSource<DurableTaskSchedulerClientOptions> changeSource = new(Options.DefaultName);
    services.AddSingleton<IOptionsChangeTokenSource<DurableTaskSchedulerClientOptions>>(changeSource);

    await using ServiceProvider provider = services.BuildServiceProvider();
    IOptionsMonitor<GrpcDurableTaskClientOptions> monitor =
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskClientOptions>>();

    GrpcChannel initialChannel = monitor.Get(Options.DefaultName).Channel!;

    // Signal a change but the actual option values are identical.
    changeSource.SignalChange();

    GrpcChannel sameChannel = monitor.Get(Options.DefaultName).Channel!;

    if (ReferenceEquals(initialChannel, sameChannel))
    {
        Console.WriteLine("  ✅ PASS: Channel retained when options unchanged");
        passCount++;
    }
    else
    {
        Console.WriteLine("  ❌ FAIL: Channel was unnecessarily invalidated");
        failCount++;
    }
}

// === Check 3: Worker channel invalidation on endpoint change ===
Console.WriteLine("[Check 3] Worker: options change invalidates cached channel...");
{
    ServiceCollection services = new();
    DefaultAzureCredential credential = new();
    string[] mutableEndpoint = ["myendpoint.westus3.durabletask.io"];

    new StubWorkerBuilder(services).UseDurableTaskScheduler(
        "myendpoint.westus3.durabletask.io", "default", credential);

    services.PostConfigure<DurableTaskSchedulerWorkerOptions>(Options.DefaultName, o =>
    {
        o.EndpointAddress = mutableEndpoint[0];
    });

    ManualChangeTokenSource<DurableTaskSchedulerWorkerOptions> changeSource = new(Options.DefaultName);
    services.AddSingleton<IOptionsChangeTokenSource<DurableTaskSchedulerWorkerOptions>>(changeSource);

    await using ServiceProvider provider = services.BuildServiceProvider();
    IOptionsMonitor<GrpcDurableTaskWorkerOptions> monitor =
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();

    GrpcChannel initialChannel = monitor.Get(Options.DefaultName).Channel!;

    mutableEndpoint[0] = "changed.westus3.durabletask.io";
    changeSource.SignalChange();

    GrpcChannel newChannel = monitor.Get(Options.DefaultName).Channel!;

    if (!ReferenceEquals(initialChannel, newChannel))
    {
        Console.WriteLine("  ✅ PASS: Channel was invalidated after endpoint change");
        passCount++;
    }
    else
    {
        Console.WriteLine("  ❌ FAIL: Channel was NOT invalidated after endpoint change");
        failCount++;
    }
}

// === Check 4: Worker channel retention when options unchanged ===
Console.WriteLine("[Check 4] Worker: unchanged options retain cached channel...");
{
    ServiceCollection services = new();
    DefaultAzureCredential credential = new();

    // Set a fixed WorkerId to ensure cache key stability across re-resolutions.
    // The default WorkerId includes Guid.NewGuid() which differs per options instance.
    new StubWorkerBuilder(services).UseDurableTaskScheduler(
        "myendpoint.westus3.durabletask.io", "default", credential, options =>
    {
        options.WorkerId = "fixed-worker-id";
    });

    ManualChangeTokenSource<DurableTaskSchedulerWorkerOptions> changeSource = new(Options.DefaultName);
    services.AddSingleton<IOptionsChangeTokenSource<DurableTaskSchedulerWorkerOptions>>(changeSource);

    await using ServiceProvider provider = services.BuildServiceProvider();
    IOptionsMonitor<GrpcDurableTaskWorkerOptions> monitor =
        provider.GetRequiredService<IOptionsMonitor<GrpcDurableTaskWorkerOptions>>();

    GrpcChannel initialChannel = monitor.Get(Options.DefaultName).Channel!;

    changeSource.SignalChange();

    GrpcChannel sameChannel = monitor.Get(Options.DefaultName).Channel!;

    if (ReferenceEquals(initialChannel, sameChannel))
    {
        Console.WriteLine("  ✅ PASS: Channel retained when options unchanged");
        passCount++;
    }
    else
    {
        Console.WriteLine("  ❌ FAIL: Channel was unnecessarily invalidated");
        failCount++;
    }
}

Console.WriteLine();
Console.WriteLine("=== VERIFICATION RESULT ===");
Console.WriteLine("Issue: #632");
Console.WriteLine("Scenario: gRPC channel cache invalidation on runtime options change");
Console.WriteLine($"Checks passed: {passCount}");
Console.WriteLine($"Checks failed: {failCount}");
Console.WriteLine($"Passed: {failCount == 0}");
Console.WriteLine($"Timestamp: {DateTime.UtcNow:O}");
Console.WriteLine("=== END RESULT ===");

Environment.Exit(failCount == 0 ? 0 : 1);

// --- Helper types ---

/// <summary>
/// Minimal stub of <see cref="IDurableTaskClientBuilder"/> for standalone verification.
/// Provides the <see cref="IServiceCollection"/> to UseDurableTaskScheduler extension methods
/// without requiring the full DI registration from AddDurableTaskClient.
/// </summary>
sealed class StubClientBuilder(IServiceCollection services) : IDurableTaskClientBuilder
{
    /// <inheritdoc/>
    public string Name => Options.DefaultName;

    /// <inheritdoc/>
    public IServiceCollection Services => services;

    /// <inheritdoc/>
    public Type? BuildTarget { get; set; }

    /// <inheritdoc/>
    public DurableTaskClient Build(IServiceProvider serviceProvider) => throw new NotSupportedException();
}

/// <summary>
/// Minimal stub of <see cref="IDurableTaskWorkerBuilder"/> for standalone verification.
/// Provides the <see cref="IServiceCollection"/> to UseDurableTaskScheduler extension methods
/// without requiring the full DI registration from AddDurableTaskWorker.
/// </summary>
sealed class StubWorkerBuilder(IServiceCollection services) : IDurableTaskWorkerBuilder
{
    /// <inheritdoc/>
    public string Name => Options.DefaultName;

    /// <inheritdoc/>
    public IServiceCollection Services => services;

    /// <inheritdoc/>
    public Type? BuildTarget { get; set; }

    /// <inheritdoc/>
    public IHostedService Build(IServiceProvider serviceProvider) => throw new NotSupportedException();
}

/// <summary>
/// A manual change token source that allows programmatic triggering of options reloads.
/// Simulates runtime configuration changes (e.g., appsettings.json reload, Azure App Configuration update).
/// </summary>
sealed class ManualChangeTokenSource<T> : IOptionsChangeTokenSource<T>
{
    CancellationTokenSource cts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ManualChangeTokenSource{T}"/> class.
    /// </summary>
    /// <param name="name">The options name to scope change notifications to.</param>
    public ManualChangeTokenSource(string? name = null)
    {
        this.Name = name ?? Options.DefaultName;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IChangeToken GetChangeToken() => new CancellationChangeToken(this.cts.Token);

    /// <summary>
    /// Signals a configuration change, causing IOptionsMonitor to re-resolve options.
    /// </summary>
    public void SignalChange()
    {
        CancellationTokenSource oldCts = Interlocked.Exchange(ref this.cts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();
    }
}
