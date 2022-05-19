// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AzureFunctionsApp
{
    using Microsoft.Extensions.Hosting;

    public class Program
    {
        public static void Main()
        {
            IHost host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .Build();

            host.Run();
        }
    }
}