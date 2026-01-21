// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace InProcessTestHost.Tests;

// This file contains test helper types for WebApplicationFactoryIntegrationTests.
// It includes sample entities, repositories, services, orchestrators, and activities
// that demonstrate how to use AddInMemoryDurableTask() with dependency injection.
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class OrderInput
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
}

public class OrderOutput
{
    public bool Success { get; set; }
    public string Status { get; set; } = "";
}

public interface IUserRepository
{
    void Add(User user);
    User? GetById(int id);
}

public class InMemoryUserRepository : IUserRepository
{
    readonly ConcurrentDictionary<int, User> users = new();

    public void Add(User user) => this.users[user.Id] = user;
    public User? GetById(int id) => this.users.TryGetValue(id, out var u) ? u : null;
}

public interface IOrderRepository
{
    void Add(Order order);
    Order? GetById(int id);
    void Update(Order order);
}

public class InMemoryOrderRepository : IOrderRepository
{
    readonly ConcurrentDictionary<int, Order> orders = new();

    public void Add(Order order) => this.orders[order.Id] = order;
    public Order? GetById(int id) => this.orders.TryGetValue(id, out var o) ? o : null;
    public void Update(Order order) => this.orders[order.Id] = order;
}

public interface IUserService
{
    UserDto? GetUser(int id);
}

public class UserService : IUserService
{
    readonly IUserRepository userRepository;

    public UserService(IUserRepository userRepository) => this.userRepository = userRepository;

    public UserDto? GetUser(int id)
    {
        var u = this.userRepository.GetById(id);
        return u == null ? null : new UserDto { Id = u.Id, Name = u.Name, Email = u.Email };
    }
}

public interface IOrderService
{
    Order? GetOrder(int id);
    void UpdateStatus(int id, string status);
}

public class OrderService : IOrderService
{
    readonly IOrderRepository orderRepository;

    public OrderService(IOrderRepository orderRepository) => this.orderRepository = orderRepository;

    public Order? GetOrder(int id) => this.orderRepository.GetById(id);

    public void UpdateStatus(int id, string status)
    {
        var order = this.orderRepository.GetById(id);
        if (order != null)
        {
            order.Status = status;
            this.orderRepository.Update(order);
        }
    }
}

public interface IPaymentService
{
    bool ProcessPayment(int orderId, decimal amount);
}

public class PaymentService : IPaymentService
{
    readonly ILogger<PaymentService> logger;

    public PaymentService(ILogger<PaymentService> logger) => this.logger = logger;

    public bool ProcessPayment(int orderId, decimal amount)
    {
        this.logger.LogInformation("Processing payment of {Amount} for order {OrderId}", amount, orderId);
        return true;
    }
}

public class UserLookupOrchestrator : TaskOrchestrator<int, UserDto?>
{
    public override async Task<UserDto?> RunAsync(TaskOrchestrationContext ctx, int userId)
    {
        return await ctx.CallActivityAsync<UserDto?>(nameof(GetUserActivity), userId);
    }
}

public class OrderProcessingOrchestrator : TaskOrchestrator<OrderInput, OrderOutput>
{
    public override async Task<OrderOutput> RunAsync(TaskOrchestrationContext ctx, OrderInput input)
    {
        var isValid = await ctx.CallActivityAsync<bool>(nameof(ValidateOrderActivity), input);
        if (!isValid)
        {
            return new OrderOutput { Success = false, Status = "Invalid" };
        }

        var paid = await ctx.CallActivityAsync<bool>(nameof(ProcessPaymentActivity), input.OrderId);
        if (!paid)
        {
            return new OrderOutput { Success = false, Status = "PaymentFailed" };
        }

        await ctx.CallActivityAsync(nameof(UpdateOrderStatusActivity), input.OrderId);
        return new OrderOutput { Success = true, Status = "Completed" };
    }
}

public class GetUserActivity : TaskActivity<int, UserDto?>
{
    readonly IUserService userService;

    public GetUserActivity(IUserService userService) => this.userService = userService;

    public override Task<UserDto?> RunAsync(TaskActivityContext ctx, int userId)
    {
        return Task.FromResult(this.userService.GetUser(userId));
    }
}

public class ValidateOrderActivity : TaskActivity<OrderInput, bool>
{
    readonly IOrderService orderService;

    public ValidateOrderActivity(IOrderService orderService) => this.orderService = orderService;

    public override Task<bool> RunAsync(TaskActivityContext ctx, OrderInput input)
    {
        var order = this.orderService.GetOrder(input.OrderId);
        return Task.FromResult(order != null && order.UserId == input.UserId && order.Amount > 0);
    }
}

public class ProcessPaymentActivity : TaskActivity<int, bool>
{
    readonly IOrderService orderService;
    readonly IPaymentService paymentService;

    public ProcessPaymentActivity(IOrderService orderService, IPaymentService paymentService)
    {
        this.orderService = orderService;
        this.paymentService = paymentService;
    }

    public override Task<bool> RunAsync(TaskActivityContext ctx, int orderId)
    {
        var order = this.orderService.GetOrder(orderId);
        if (order == null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(this.paymentService.ProcessPayment(orderId, order.Amount));
    }
}

public class UpdateOrderStatusActivity : TaskActivity<int, object?>
{
    readonly IOrderService orderService;

    public UpdateOrderStatusActivity(IOrderService orderService) => this.orderService = orderService;

    public override Task<object?> RunAsync(TaskActivityContext ctx, int orderId)
    {
        this.orderService.UpdateStatus(orderId, "Completed");
        return Task.FromResult<object?>(null);
    }
}
