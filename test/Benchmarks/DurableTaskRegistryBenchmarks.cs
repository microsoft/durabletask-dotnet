// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Dapr.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Dapr.DurableTask.Benchmarks;

[MemoryDiagnoser]
public class DurableTaskRegistryBenchmarks
{
    readonly IServiceProvider services;

    public DurableTaskRegistryBenchmarks()
    {
        ServiceCollection services = new();
        services.AddTransient<FromServicesActivity>();
        this.services = services.BuildServiceProvider();
    }

    [Benchmark]
    public void CreateActivity_FromServices()
    {
        DurableTaskRegistry registry = new();
        TaskName name = nameof(FromServicesActivity);
        registry.AddActivity<FromServicesActivity>(name);
        IDurableTaskFactory factory = registry.BuildFactory();

        for (int i = 0; i < 100; i++)
        {
            factory.TryCreateActivity(name, this.services, out _);
        }
    }

    [Benchmark]
    public void CreateActivity_FromActivator()
    {
        DurableTaskRegistry registry = new();
        TaskName name = nameof(FromActivatorActivity);
        registry.AddActivity<FromActivatorActivity>(name);
        IDurableTaskFactory factory = registry.BuildFactory();

        for (int i = 0; i < 100; i++)
        {
            factory.TryCreateActivity(name, this.services, out _);
        }
    }

    class FromServicesActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            throw new NotImplementedException();
        }
    }

    class FromActivatorActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            throw new NotImplementedException();
        }
    }
}
