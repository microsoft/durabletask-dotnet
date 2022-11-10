// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations
{
    [DurableTask("CheckInventory")]
    public class CheckInventoryActivity : TaskActivity<OrderInfo, bool>
    {
        readonly ILogger logger;

        // Dependencies are injected from ASP.NET host service container
        public CheckInventoryActivity(ILogger<CheckInventoryActivity> logger)
        {
            this.logger = logger;
        }

        public override Task<bool> RunAsync(TaskActivityContext context, OrderInfo orderInfo)
        {
            if (orderInfo == null)
            {
                throw new ArgumentException("Failed to read order info!");
            }

            this.logger.LogInformation(
                "{instanceId}: Checking inventory for '{item}'...found some!",
                context.InstanceId,
                orderInfo.Item);
            return Task.FromResult(true);
        }
    }
}
