using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace InProcessTestHost.Tests;

/// <summary>
/// Tests for AddInMemoryDurableTask() extension which allows injecting 
/// InMemoryOrchestrationService into an existing host (e.g., WebApplicationFactory).
/// </summary>
public class WebApplicationFactoryIntegrationTests : IAsyncLifetime
{
    IHost host = null!;
    DurableTaskClient client = null!;

    public async Task InitializeAsync()
    {
        this.host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IUserRepository, InMemoryUserRepository>();
                services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
                services.AddScoped<IUserService, UserService>();
                services.AddScoped<IOrderService, OrderService>();
                services.AddScoped<IPaymentService, PaymentService>();
                services.AddLogging(logging => logging.AddDebug());

                services.AddInMemoryDurableTask(tasks =>
                {
                    tasks.AddOrchestrator<UserLookupOrchestrator>();
                    tasks.AddOrchestrator<OrderProcessingOrchestrator>();
                    tasks.AddActivity<GetUserActivity>();
                    tasks.AddActivity<ValidateOrderActivity>();
                    tasks.AddActivity<ProcessPaymentActivity>();
                    tasks.AddActivity<UpdateOrderStatusActivity>();
                });
            })
            .Build();

        await this.host.StartAsync();
        this.client = this.host.Services.GetRequiredService<DurableTaskClient>();
    }

    public async Task DisposeAsync()
    {
        await this.host.StopAsync();
        this.host.Dispose();
    }

    /// <summary>
    /// Verifies activities can resolve services from the host's DI container.
    /// </summary>
    [Fact]
    public async Task Activity_ResolvesServices_FromDIContainer()
    {
        var userRepo = this.host.Services.GetRequiredService<IUserRepository>();
        userRepo.Add(new User { Id = 1, Name = "Alice", Email = "alice@example.com" });

        var instanceId = await this.client.ScheduleNewOrchestrationInstanceAsync(nameof(UserLookupOrchestrator), 1);
        var result = await this.client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        var user = result.ReadOutputAs<UserDto>();
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Name);
    }

    /// <summary>
    /// Verifies a multi-step orchestration can use multiple services from DI.
    /// </summary>
    [Fact]
    public async Task ComplexOrchestration_UsesMultipleServicesFromDI()
    {
        var userRepo = this.host.Services.GetRequiredService<IUserRepository>();
        var orderRepo = this.host.Services.GetRequiredService<IOrderRepository>();
        userRepo.Add(new User { Id = 1, Name = "Bob", Email = "bob@test.com" });
        orderRepo.Add(new Order { Id = 100, UserId = 1, Amount = 99.99m, Status = "Pending" });

        var instanceId = await this.client.ScheduleNewOrchestrationInstanceAsync(
            nameof(OrderProcessingOrchestrator),
            new OrderInput { OrderId = 100, UserId = 1 });
        var result = await this.client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: true);

        Assert.NotNull(result);
        Assert.Equal(OrchestrationRuntimeStatus.Completed, result.RuntimeStatus);
        var output = result.ReadOutputAs<OrderOutput>();
        Assert.NotNull(output);
        Assert.True(output.Success);
        Assert.Equal("Completed", output.Status);

        var order = orderRepo.GetById(100);
        Assert.Equal("Completed", order?.Status);
    }

    /// <summary>
    /// Verifies InMemoryOrchestrationService is accessible from DI.
    /// </summary>
    [Fact]
    public async Task InMemoryOrchestrationService_IsAccessible()
    {
        var orchestrationService = this.host.Services.GetInMemoryOrchestrationService();
        Assert.NotNull(orchestrationService);
    }
}
