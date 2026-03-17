// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AzureFunctionsSmokeTests;

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
                        .AddOrchestrator<GeneratedOrchestration>()
                        .AddOrchestrator<ChildGeneratedOrchestration>()
                        .AddActivity<CountCharactersActivity>()
                        .AddEntity<GeneratorCounter>();
                });
            })
            .Build();

        host.Run();
    }
}
