//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using DurableTask;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Models;

namespace WebAPI.Controllers;

[Route("orders")]
[ApiController]
public class OrderProcessingController : ControllerBase
{
    readonly TaskHubClient taskHubClient;

    public OrderProcessingController(TaskHubClient taskHubClient)
    {
        this.taskHubClient = taskHubClient;
    }

    // HTTPie command:
    // http POST http://localhost:8080/orders/new Item=catfood Quantity=5 Price=6000
    [HttpPost("new")]
    public async Task<ActionResult> CreateOrder([FromBody] OrderInfo orderInfo)
    {
        if (orderInfo == null || orderInfo.Item == null)
        {
            return this.BadRequest(new { error = "No item information was included in the order" });
        }

        // Generate an order ID and start the order processing workflow orchestration
        string orderId = $"{orderInfo.Item}-{Guid.NewGuid().ToString()[..4]}";
        await this.taskHubClient.ScheduleNewOrchestrationInstanceAsync(
            orchestratorName: "ProcessOrder",
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
        OrchestrationMetadata? metadata = await this.taskHubClient.GetInstanceMetadataAsync(
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
