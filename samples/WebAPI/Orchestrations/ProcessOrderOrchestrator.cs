// ----------------------------------------------------------------------------------
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using DurableTask;
using WebAPI.Models;

namespace WebAPI.Orchestrations;

public class ProcessOrderOrchestrator : TaskOrchestratorBase<OrderInfo, OrderStatus>
{
    protected override async Task<OrderStatus> OnRunAsync(OrderInfo? orderInfo)
    {
        if (orderInfo == null)
        {
            // Unhandled exceptions transition the orchestration into a failed state. 
            throw new InvalidOperationException("Failed to read the order info!");
        }

        // Call the following activity operations in sequence.
        OrderStatus orderStatus = new();
        if (await this.Context.CallCheckInventoryAsync(orderInfo))
        {
            // Orders over $1,000 require manual approval. We use a custom status
            // value to communicate this back to the client application.
            bool requiresApproval = orderInfo.Price > 1000.00;
            this.Context.SetCustomStatus(new { requiresApproval });

            if (requiresApproval)
            {
                orderStatus.RequiresApproval = true;

                ApprovalEvent approvalEvent;
                try
                {
                    // Wait for the client application to send an approval event.
                    // Auto-reject if an approval isn't received in 10 seconds.
                    approvalEvent = await this.Context.WaitForExternalEvent<ApprovalEvent>(
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

            await this.Context.CallChargeCustomerAsync(orderInfo);
            await this.Context.CallCreateShipmentAsync(orderInfo);

            return orderStatus;
        }

        return orderStatus;
    }
}
