// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

[DurableTask("ChargeCustomer")]
public class ChargeCustomerActivity(ILogger<ChargeCustomerActivity> logger) : TaskActivity<OrderInfo, object?>
{
    readonly ILogger logger = logger;

    public override async Task<object?> RunAsync(TaskActivityContext context, OrderInfo orderInfo)
    {
        this.logger.LogInformation(
            "{instanceId}: Charging customer {price:C}'...",
            context.InstanceId,
            orderInfo?.Price ?? 0.0);

        await Task.Delay(TimeSpan.FromSeconds(3));
        return null;
    }
}
