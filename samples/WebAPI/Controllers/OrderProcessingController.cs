// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Models;

namespace WebAPI.Controllers;

[Route("orders")]
[ApiController]
public class OrderProcessingController : ControllerBase
{
    readonly DurableTaskClient durableTaskClient;

    public OrderProcessingController(DurableTaskClient durableTaskClient)
    {
        this.durableTaskClient = durableTaskClient;
    }

    // HTTPie command:
    // http POST http://localhost:8080/orders/new Item=catfood Quantity=5 Price=600
    [HttpPost("new")]
    public async Task<ActionResult> CreateOrder([FromBody] OrderInfo orderInfo)
    {
        if (orderInfo == null || orderInfo.Item == null)
        {
            return this.BadRequest(new { error = "No item information was included in the order" });
        }

        // Generate an order ID and start the order processing workflow orchestration
        string orderId = $"{orderInfo.Item}-{Guid.NewGuid().ToString()[..4]}";
        await this.durableTaskClient.ScheduleNewProcessOrderOrchestratorInstanceAsync(
            instanceId: orderId,
            input: orderInfo);

        // Return 202 with a link to the GetOrderStatus API
        return this.AcceptedAtAction(
            actionName: nameof(GetOrderStatus),
            controllerName: null,
            routeValues: new { orderId },
            value: new { orderId });
    }

    [HttpGet("{orderId}")]
    public async Task<ActionResult> GetOrderStatus(string orderId)
    {
        OrchestrationMetadata? metadata = await this.durableTaskClient.GetInstanceMetadataAsync(
            instanceId: orderId,
            getInputsAndOutputs: true);

        // TODO: Include a link to the approval API

        return this.Ok(new
        {
            Found = metadata != null,
            Metadata = metadata,
        });
    }
}
