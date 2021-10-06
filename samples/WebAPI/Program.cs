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
using WebAPI.Orchestrations;

var builder = WebApplication.CreateBuilder(args);

// Define the orchestration and activities.
// TODO: Allow the orchestrators and activities to be auto-discovered via reflection (or codegen if we want to get really crazy)
builder.Services.AddDurableTask(orchestrationBuilder =>
    orchestrationBuilder
        .AddOrchestrator<ProcessOrderOrchestrator>()
        .AddActivity<CheckInventoryActivity>()
        .AddActivity<ChargeCustomerActivity>()
        .AddActivity<CreateShipmentActivity>());

// Configure the HTTP request pipeline.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

WebApplication app = builder.Build();
app.MapControllers();
app.Run("http://localhost:8080");
