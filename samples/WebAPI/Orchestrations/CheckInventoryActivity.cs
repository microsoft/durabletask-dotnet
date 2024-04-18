﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

[DurableTask("CheckInventory")]
public class CheckInventoryActivity(ILogger<CheckInventoryActivity> logger)
    : TaskActivity<OrderInfo, bool>
{
    public override Task<bool> RunAsync(TaskActivityContext context, OrderInfo orderInfo)
    {
        ArgumentNullException.ThrowIfNull(context);
        logger.LogInformation(
            "{instanceId}: Checking inventory for '{item}'...found some!",
            context.InstanceId,
            orderInfo.Item);
        return Task.FromResult(true);
    }
}
