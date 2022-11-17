// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

[DurableTask]
public class ProcessOrderOrchestrator : TaskOrchestrator<OrderInfo, OrderStatus>
{
    public override async Task<OrderStatus> RunAsync(TaskOrchestrationContext context, OrderInfo? orderInfo)
    {
        if (orderInfo == null)
        {
            // Unhandled exceptions transition the orchestration into a failed state. 
            throw new InvalidOperationException("Failed to read the order info!");
        }

        // Call the following activity operations in sequence.
        OrderStatus orderStatus = new();
        if (await context.CallCheckInventoryAsync(orderInfo))
        {
            // Orders over $1,000 require manual approval. We use a custom status
            // value to communicate this back to the client application.
            bool requiresApproval = orderInfo.Price > 1000.00;
            context.SetCustomStatus(new { requiresApproval });

            if (requiresApproval)
            {
                orderStatus.RequiresApproval = true;

                ApprovalEvent approvalEvent;
                try
                {
                    // Wait for the client application to send an approval event.
                    // Auto-reject if an approval isn't received in 10 seconds.
                    approvalEvent = await context.WaitForExternalEvent<ApprovalEvent>(
                        eventName: "Approve",
                        timeout: TimeSpan.FromSeconds(10));
                }
                catch (TaskCanceledException)
                {
                    approvalEvent = new ApprovalEvent { IsApproved = false };
                }

                orderStatus.Approval = approvalEvent;
                if (!approvalEvent.IsApproved)
                {
                    return orderStatus;
                }
            }

            await context.CallChargeCustomerAsync(orderInfo);
            await context.CallCreateShipmentAsync(orderInfo);

            return orderStatus;
        }

        return orderStatus;
    }
}
