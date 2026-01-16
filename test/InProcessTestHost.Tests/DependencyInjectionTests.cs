// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests to verify DurableTaskTestHost works with dependency injection.
/// </summary>
public class DependencyInjectionTests
{
    /// <summary>
    /// Verifies an activity can resolve a service registered via ConfigureServices.
    /// </summary>
    [Fact]
    public async Task Activity_CanResolveService_FromDI()
    {
        await using var host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestrator<GreetingOrchestrator>();
                tasks.AddActivity<GreetingActivity>();
            },
            new DurableTaskTestHostOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton<IGreetingService, GreetingService>();
                }
            });

        var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(GreetingOrchestrator),
            "World");

        var result = await host.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        var output = result.ReadOutputAs<string>();
        Assert.Equal("Hello, World! (from DI service)", output);
    }

    /// <summary>
    /// Verifies multiple activities can share the same DI-registered service.
    /// </summary>
    [Fact]
    public async Task Activity_CanUseMultipleServices_FromDI()
    {
        await using var host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestrator<CalculatorOrchestrator>();
                tasks.AddActivity<AddActivity>();
                tasks.AddActivity<MultiplyActivity>();
            },
            new DurableTaskTestHostOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton<ICalculatorService, CalculatorService>();
                }
            });

        var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(CalculatorOrchestrator),
            new CalculatorInput { A = 5, B = 3 });

        var result = await host.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        var output = result.ReadOutputAs<CalculatorOutput>();
        Assert.NotNull(output);
        Assert.Equal(8, output.Sum);      // 5 + 3 = 8
        Assert.Equal(15, output.Product); // 5 * 3 = 15
    }

    /// <summary>
    /// Verifies host.Services exposes the DI container for direct service access.
    /// </summary>
    [Fact]
    public async Task Services_Property_AllowsAccessToRegisteredServices()
    {
        await using var host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestrator<GreetingOrchestrator>();
                tasks.AddActivity<GreetingActivity>();
            },
            new DurableTaskTestHostOptions
            {
                ConfigureServices = services =>
                {
                    services.AddSingleton<IGreetingService, GreetingService>();
                }
            });

        var greetingService = host.Services.GetRequiredService<IGreetingService>();

        Assert.NotNull(greetingService);
        Assert.Equal("Hello, Test! (from DI service)", greetingService.Greet("Test"));
    }

    /// <summary>
    /// Verifies scoped services get a fresh instance per activity execution.
    /// </summary>
    [Fact]
    public async Task ScopedServices_AreResolvedPerActivityExecution()
    {
        await using var host = await DurableTaskTestHost.StartAsync(
            tasks =>
            {
                tasks.AddOrchestrator<CountingOrchestrator>();
                tasks.AddActivity<CountingActivity>();
            },
            new DurableTaskTestHostOptions
            {
                ConfigureServices = services =>
                {
                    services.AddScoped<ICounterService, CounterService>();
                }
            });

        var instanceId = await host.Client.ScheduleNewOrchestrationInstanceAsync(
            nameof(CountingOrchestrator));

        var result = await host.Client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);

        var output = result.ReadOutputAs<int[]>();
        Assert.NotNull(output);
        Assert.Equal(2, output.Length);
        Assert.Equal(1, output[0]);
        Assert.Equal(1, output[1]);
    }

    #region Test Services

    public interface IGreetingService
    {
        string Greet(string name);
    }

    public class GreetingService : IGreetingService
    {
        public string Greet(string name) => $"Hello, {name}! (from DI service)";
    }

    public interface ICalculatorService
    {
        int Add(int a, int b);
        int Multiply(int a, int b);
    }

    public class CalculatorService : ICalculatorService
    {
        public int Add(int a, int b) => a + b;
        public int Multiply(int a, int b) => a * b;
    }

    public interface ICounterService
    {
        int Increment();
    }

    public class CounterService : ICounterService
    {
        private int count;
        public int Increment() => ++count;
    }

    #endregion

    #region Test DTOs

    public class CalculatorInput
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    public class CalculatorOutput
    {
        public int Sum { get; set; }
        public int Product { get; set; }
    }

    #endregion

    #region Test Orchestrators and Activities

    /// <summary>
    /// Orchestrator that calls an activity with DI dependencies.
    /// </summary>
    public class GreetingOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string name)
        {
            return await context.CallActivityAsync<string>(nameof(GreetingActivity), name);
        }
    }

    /// <summary>
    /// Activity that uses a service resolved from DI.
    /// </summary>
    public class GreetingActivity : TaskActivity<string, string>
    {
        private readonly IGreetingService greetingService;

        public GreetingActivity(IGreetingService greetingService)
        {
            this.greetingService = greetingService;
        }

        public override Task<string> RunAsync(TaskActivityContext context, string name)
        {
            return Task.FromResult(this.greetingService.Greet(name));
        }
    }

    /// <summary>
    /// Orchestrator that calls multiple activities.
    /// </summary>
    public class CalculatorOrchestrator : TaskOrchestrator<CalculatorInput, CalculatorOutput>
    {
        public override async Task<CalculatorOutput> RunAsync(TaskOrchestrationContext context, CalculatorInput input)
        {
            var sumTask = context.CallActivityAsync<int>(nameof(AddActivity), input);
            var productTask = context.CallActivityAsync<int>(nameof(MultiplyActivity), input);

            await Task.WhenAll(sumTask, productTask);

            return new CalculatorOutput
            {
                Sum = sumTask.Result,
                Product = productTask.Result
            };
        }
    }

    public class AddActivity : TaskActivity<CalculatorInput, int>
    {
        private readonly ICalculatorService calculator;

        public AddActivity(ICalculatorService calculator)
        {
            this.calculator = calculator;
        }

        public override Task<int> RunAsync(TaskActivityContext context, CalculatorInput input)
        {
            return Task.FromResult(this.calculator.Add(input.A, input.B));
        }
    }

    public class MultiplyActivity : TaskActivity<CalculatorInput, int>
    {
        private readonly ICalculatorService calculator;

        public MultiplyActivity(ICalculatorService calculator)
        {
            this.calculator = calculator;
        }

        public override Task<int> RunAsync(TaskActivityContext context, CalculatorInput input)
        {
            return Task.FromResult(this.calculator.Multiply(input.A, input.B));
        }
    }

    /// <summary>
    /// Orchestrator that tests scoped services.
    /// </summary>
    public class CountingOrchestrator : TaskOrchestrator<object?, int[]>
    {
        public override async Task<int[]> RunAsync(TaskOrchestrationContext context, object? input)
        {
            // Call the same activity twice
            var count1 = await context.CallActivityAsync<int>(nameof(CountingActivity));
            var count2 = await context.CallActivityAsync<int>(nameof(CountingActivity));

            return new[] { count1, count2 };
        }
    }

    public class CountingActivity : TaskActivity<object?, int>
    {
        private readonly ICounterService counter;

        public CountingActivity(ICounterService counter)
        {
            this.counter = counter;
        }

        public override Task<int> RunAsync(TaskActivityContext context, object? input)
        {
            return Task.FromResult(this.counter.Increment());
        }
    }

    #endregion
}
