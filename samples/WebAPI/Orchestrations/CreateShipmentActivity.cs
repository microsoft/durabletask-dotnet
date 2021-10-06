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

[DurableTask("CreateShipment")]
public class CreateShipmentActivity : TaskActivityBase<OrderInfo, object>
{
    readonly ILogger logger;

    // Dependencies are injected from ASP.NET host service container
    public CreateShipmentActivity(ILogger<CreateShipmentActivity> logger)
    {
        this.logger = logger;
    }

    protected override async Task<object?> OnRunAsync(OrderInfo? orderInfo)
    {
        this.logger.LogInformation(
            "{instanceId}: Shipping customer order of {quantity} {item}(s)...",
            this.Context.InstanceId,
            orderInfo?.Quantity ?? 0,
            orderInfo?.Item);

        await Task.Delay(TimeSpan.FromSeconds(3));
        return null;
    }
}
