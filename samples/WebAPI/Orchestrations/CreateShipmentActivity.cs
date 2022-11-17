// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

[DurableTask("CreateShipment")]
public class CreateShipmentActivity : TaskActivity<OrderInfo, object?>
{
    readonly ILogger logger;

    // Dependencies are injected from ASP.NET host service container
    public CreateShipmentActivity(ILogger<CreateShipmentActivity> logger)
    {
        this.logger = logger;
    }

    public override async Task<object?> RunAsync(TaskActivityContext context, OrderInfo orderInfo)
    {
        this.logger.LogInformation(
            "{instanceId}: Shipping customer order of {quantity} {item}(s)...",
            context.InstanceId,
            orderInfo?.Quantity ?? 0,
            orderInfo?.Item);

        await Task.Delay(TimeSpan.FromSeconds(3));
        return null;
    }
}
