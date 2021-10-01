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

using System.Text.Json.Serialization;
using DurableTask;
using WebAPI.Models;

var builder = WebApplication.CreateBuilder(args);

// Define the orchestration and activities.
// CONSIDER: Move the orchestration logic into a separate file
builder.Services.AddDurableTask(orchestrationBuilder =>
    orchestrationBuilder
        .AddTaskOrchestrator("ProcessOrder", async ctx =>
        {
            OrderInfo? orderInfo = ctx.GetInput<OrderInfo>();
            if (orderInfo == null)
            {
                // Unhandled exceptions transition the orchestration into a failed state. 
                throw new InvalidOperationException("Failed to read the order info!");
            }

            // Call the following activity operations in sequence.
            OrderStatus orderStatus = new();
            if (await ctx.CallActivityAsync<bool>("CheckInventory", orderInfo))
            {
                // Orders over $1,000 require manual approval. We use a custom status
                // value to communicate this back to the client application.
                bool requiresApproval = orderInfo.Price > 1000.00;
                ctx.SetCustomStatus(new { requiresApproval });

                if (requiresApproval)
                {
                    orderStatus.RequiresApproval = true;

                    ApprovalEvent approvalEvent;
                    try
                    {
                        // Wait for the client application to send an approval event.
                        // Auto-reject if an approval isn't received in 10 seconds.
                        approvalEvent = await ctx.WaitForExternalEvent<ApprovalEvent>(
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

                await ctx.CallActivityAsync("ChargeCustomer", orderInfo);
                await ctx.CallActivityAsync("CreateShipment", orderInfo);

                return orderStatus;
            }

            return orderStatus;
        })
        .AddTaskActivity("CheckInventory", ctx =>
        {
            // Exercise for the reader: implement check-inventory logic
            return true;
        })
        .AddTaskActivity("ChargeCustomer", ctx =>
        {
            // Exercise for the reader: implement charge-customer logic
            return Task.Delay(TimeSpan.FromSeconds(3));
        })
        .AddTaskActivity("CreateShipment", ctx =>
        {
            // Exercise for the reader: implement create-shipment logic
            return Task.Delay(TimeSpan.FromSeconds(3));
        }));

// Configure the HTTP request pipeline.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

WebApplication app = builder.Build();
app.MapControllers();
app.Run("http://localhost:8080");
