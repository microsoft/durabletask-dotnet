// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using AzureFunctionsApp.Approval;
using AzureFunctionsApp.Entities;
using AzureFunctionsApp.Typed;
using Microsoft.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AzureFunctionsApp;

public class Program
{
    public static void Main()
    {
        IHost host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureServices(services =>
            {
                services.Configure<DurableTaskRegistry>(registry =>
                {
                    registry
                        .AddOrchestrator<HelloCitiesTyped>()
                        .AddOrchestrator<ApprovalOrchestrator>()
                        .AddActivity<SayHelloTyped>()
                        .AddActivity<NotifyApprovalRequired>()
                        .AddEntity<Counter>()
                        .AddEntity<Lifetime>()
                        .AddEntity<UserEntity>();
                });
            })
            .Build();

        host.Run();
    }
}