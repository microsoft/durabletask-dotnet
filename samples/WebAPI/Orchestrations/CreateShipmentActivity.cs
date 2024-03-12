// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

[DurableTask("CreateShipment")]
public class CreateShipmentActivity(ILogger<CreateShipmentActivity> logger)
    : TaskActivity<OrderInfo, object?>
{
    public override async Task<object?> RunAsync(TaskActivityContext context, OrderInfo orderInfo)
    {
        logger.LogInformation(
            "{instanceId}: Shipping customer order of {quantity} {item}(s)...",
            context.InstanceId,
            orderInfo?.Quantity ?? 0,
            orderInfo?.Item);

        await Task.Delay(TimeSpan.FromSeconds(3));
        return null;
    }
}
