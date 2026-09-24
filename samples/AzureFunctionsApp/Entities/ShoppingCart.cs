// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

/// <summary>
/// This sample demonstrates strongly-typed entity invocation using proxy interfaces.
/// Instead of calling entities using string-based operation names, you define an interface
/// that represents the entity's operations and use it to invoke operations in a type-safe manner.
/// </summary>

/// <summary>
/// Entity proxy interface for the shopping cart entity (orchestration use).
/// Defines the operations that can be performed on a shopping cart from orchestrations.
/// </summary>
public interface IShoppingCartProxy : IEntityProxy
{
    /// <summary>
    /// Adds an item to the shopping cart.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>The total number of items in the cart.</returns>
    Task<int> AddItem(CartItem item);

    /// <summary>
    /// Removes an item from the shopping cart.
    /// </summary>
    /// <param name="itemId">The ID of the item to remove.</param>
    /// <returns>True if the item was removed, false if not found.</returns>
    Task<bool> RemoveItem(string itemId);

    /// <summary>
    /// Gets the total price of all items in the cart.
    /// </summary>
    /// <returns>The total price.</returns>
    Task<decimal> GetTotalPrice();

    /// <summary>
    /// Clears all items from the cart.
    /// </summary>
    Task Clear();
}

/// <summary>
/// Client-side proxy interface for the shopping cart entity.
/// Client operations are fire-and-forget (cannot return results).
/// </summary>
public interface IShoppingCartClientProxy : IEntityProxy
{
    /// <summary>
    /// Signals the entity to add an item to the shopping cart.
    /// </summary>
    /// <param name="item">The item to add.</param>
    Task AddItem(CartItem item);

    /// <summary>
    /// Signals the entity to clear all items from the cart.
    /// </summary>
    Task Clear();
}

/// <summary>
/// Represents an item in the shopping cart.
/// </summary>
public record CartItem(string Id, string Name, decimal Price, int Quantity);

/// <summary>
/// Shopping cart state.
/// </summary>
public record ShoppingCartState
{
    public List<CartItem> Items { get; init; } = new();
}

/// <summary>
/// Shopping cart entity implementation.
/// </summary>
[DurableTask(nameof(ShoppingCart))]
public class ShoppingCart : TaskEntity<ShoppingCartState>
{
    readonly ILogger logger;

    public ShoppingCart(ILogger<ShoppingCart> logger)
    {
        this.logger = logger;
    }

    public int AddItem(CartItem item)
    {
        CartItem? existing = this.State.Items.FirstOrDefault(i => i.Id == item.Id);
        if (existing != null)
        {
            this.State.Items.Remove(existing);
            this.State.Items.Add(existing with { Quantity = existing.Quantity + item.Quantity });
        }
        else
        {
            this.State.Items.Add(item);
        }

        this.logger.LogInformation("Added item {ItemId} to cart {CartId}. Total items: {Count}", item.Id, this.Context.Id.Key, this.State.Items.Count);
        return this.State.Items.Count;
    }

    public bool RemoveItem(string itemId)
    {
        CartItem? item = this.State.Items.FirstOrDefault(i => i.Id == itemId);
        if (item != null)
        {
            this.State.Items.Remove(item);
            this.logger.LogInformation("Removed item {ItemId} from cart {CartId}", itemId, this.Context.Id.Key);
            return true;
        }

        this.logger.LogWarning("Item {ItemId} not found in cart {CartId}", itemId, this.Context.Id.Key);
        return false;
    }

    public decimal GetTotalPrice()
    {
        decimal total = this.State.Items.Sum(i => i.Price * i.Quantity);
        this.logger.LogInformation("Cart {CartId} total price: {Total:C}", this.Context.Id.Key, total);
        return total;
    }

    public void Clear()
    {
        int count = this.State.Items.Count;
        this.State.Items.Clear();
        this.logger.LogInformation("Cleared {Count} items from cart {CartId}", count, this.Context.Id.Key);
    }
}

/// <summary>
/// Orchestration that demonstrates strongly-typed entity invocation.
/// </summary>
public static class ShoppingCartOrchestration
{
    [Function(nameof(ProcessShoppingCartOrder))]
    public static async Task<OrderResult> ProcessShoppingCartOrder(
        [OrchestrationTrigger] TaskOrchestrationContext context,
        string cartId)
    {
        ILogger logger = context.CreateReplaySafeLogger(nameof(ProcessShoppingCartOrder));

        // Create a strongly-typed proxy for the shopping cart entity
        EntityInstanceId entityId = new(nameof(ShoppingCart), cartId);
        IShoppingCartProxy cart = context.Entities.CreateProxy<IShoppingCartProxy>(entityId);

        // Add some items to the cart using strongly-typed method calls
        logger.LogInformation("Adding items to cart {CartId}", cartId);
        await cart.AddItem(new CartItem("ITEM001", "Laptop", 999.99m, 1));
        await cart.AddItem(new CartItem("ITEM002", "Mouse", 29.99m, 2));
        await cart.AddItem(new CartItem("ITEM003", "Keyboard", 79.99m, 1));

        // Get the total price
        decimal totalPrice = await cart.GetTotalPrice();
        logger.LogInformation("Cart {CartId} total: {Total:C}", cartId, totalPrice);

        // Simulate order processing
        if (totalPrice > 1000m)
        {
            logger.LogInformation("Applying discount for cart {CartId}", cartId);
            totalPrice *= 0.9m; // 10% discount
        }

        // Clear the cart after order is processed
        await cart.Clear();
        logger.LogInformation("Cart {CartId} cleared after order processing", cartId);

        return new OrderResult(cartId, totalPrice, context.CurrentUtcDateTime);
    }

    public record OrderResult(string CartId, decimal TotalPrice, DateTime OrderDate);
}

/// <summary>
/// HTTP APIs for the shopping cart sample.
/// </summary>
public static class ShoppingCartApis
{
    /// <summary>
    /// Start an orchestration to process a shopping cart order.
    /// Usage: POST /api/shopping-cart/{cartId}/process
    /// </summary>
    [Function("ShoppingCart_ProcessOrder")]
    public static async Task<HttpResponseData> ProcessOrderAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "shopping-cart/{cartId}/process")]
        HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string cartId)
    {
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ShoppingCartOrchestration.ProcessShoppingCartOrder),
            cartId);

        return client.CreateCheckStatusResponse(request, instanceId);
    }

    /// <summary>
    /// Add an item to a shopping cart using strongly-typed proxy from client.
    /// Usage: POST /api/shopping-cart/{cartId}/items?id={id}&name={name}&price={price}&quantity={quantity}
    /// </summary>
    [Function("ShoppingCart_AddItem")]
    public static async Task<HttpResponseData> AddItemAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "shopping-cart/{cartId}/items")]
        HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string cartId)
    {
        string? itemId = request.Query["id"];
        string? name = request.Query["name"];
        if (!decimal.TryParse(request.Query["price"], out decimal price) || price <= 0)
        {
            HttpResponseData badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid price");
            return badRequest;
        }

        if (!int.TryParse(request.Query["quantity"], out int quantity) || quantity <= 0)
        {
            HttpResponseData badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Invalid quantity");
            return badRequest;
        }

        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(name))
        {
            HttpResponseData badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteStringAsync("Item ID and name are required");
            return badRequest;
        }

        // Use strongly-typed proxy for client-side entity invocation
        EntityInstanceId entityId = new(nameof(ShoppingCart), cartId);
        IShoppingCartClientProxy cart = client.Entities.CreateProxy<IShoppingCartClientProxy>(entityId);

        // Signal the entity to add the item (fire-and-forget)
        await cart.AddItem(new CartItem(itemId, name, price, quantity));

        HttpResponseData response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync($"Item {itemId} added to cart {cartId}");
        return response;
    }

    /// <summary>
    /// Get the current state of a shopping cart.
    /// Usage: GET /api/shopping-cart/{cartId}
    /// </summary>
    [Function("ShoppingCart_Get")]
    public static async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "shopping-cart/{cartId}")]
        HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string cartId)
    {
        EntityInstanceId entityId = new(nameof(ShoppingCart), cartId);
        EntityMetadata<ShoppingCartState>? entity = await client.Entities.GetEntityAsync<ShoppingCartState>(entityId);

        if (entity is null)
        {
            return request.CreateResponse(HttpStatusCode.NotFound);
        }

        HttpResponseData response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            cartId,
            entity.State.Items,
            TotalPrice = entity.State.Items.Sum(i => i.Price * i.Quantity),
            ItemCount = entity.State.Items.Count,
        });

        return response;
    }

    /// <summary>
    /// Clear a shopping cart using strongly-typed proxy.
    /// Usage: DELETE /api/shopping-cart/{cartId}
    /// </summary>
    [Function("ShoppingCart_Clear")]
    public static async Task<HttpResponseData> ClearAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "shopping-cart/{cartId}")]
        HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        string cartId)
    {
        EntityInstanceId entityId = new(nameof(ShoppingCart), cartId);
        IShoppingCartClientProxy cart = client.Entities.CreateProxy<IShoppingCartClientProxy>(entityId);

        // Signal the entity to clear the cart
        await cart.Clear();

        HttpResponseData response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteStringAsync($"Cart {cartId} cleared");
        return response;
    }
}
