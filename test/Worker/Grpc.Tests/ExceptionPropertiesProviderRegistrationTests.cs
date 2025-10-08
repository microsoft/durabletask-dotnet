// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Worker.Grpc.Tests;

public class ExceptionPropertiesProviderRegistrationTests
{
    sealed class TestExceptionPropertiesProvider : IExceptionPropertiesProvider
    {
        public IDictionary<string, object?>? GetExceptionProperties(Exception exception)
        {
            return new Dictionary<string, object?> { ["Foo"] = "Bar" };
        }
    }

    [Fact]
    public void DiRegistration_RegistersAndFlowsToWorker()
    {
        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        // Register via DI directly
        services.AddSingleton<IExceptionPropertiesProvider, TestExceptionPropertiesProvider>();

        services.AddDurableTaskWorker(builder =>
        {
            builder.UseGrpc();
        });

        using ServiceProvider sp = services.BuildServiceProvider();

        IHostedService hosted = Assert.Single(sp.GetServices<IHostedService>());
        Assert.IsType<GrpcDurableTaskWorker>(hosted);

        object? provider = typeof(DurableTaskWorker)
            .GetProperty("ExceptionPropertiesProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(hosted);

        Assert.NotNull(provider);
        Assert.IsType<TestExceptionPropertiesProvider>(provider);

        // And DI resolves the same instance
        var resolved = sp.GetRequiredService<IExceptionPropertiesProvider>();
        Assert.Same(resolved, provider);
    }
}


