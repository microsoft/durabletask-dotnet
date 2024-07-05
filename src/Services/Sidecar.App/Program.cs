// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using CommandLine;
using DurableTask.AzureStorage;
using DurableTask.Core;
using DurableTask.SqlServer;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Protobuf;
using Microsoft.DurableTask.Sidecar.Grpc;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;

namespace Microsoft.DurableTask.Sidecar.App;

static class Program
{
    public static readonly InMemoryOrchestrationService SingletonLocalOrchestrationService = new();

    // We allow stdin to be overwritten for in-process testing
    public static IInputReader InputReader { get; set; } = new StandardInputReader();

    // We allow an additional logger provider for in-process testing
    public static ILoggerProvider? AdditionalLoggerProvider { get; set; }

    public static async Task<int> Main(string[] args) =>
        await Parser.Default.ParseArguments<StartOptions>(args).MapResult(
            (StartOptions options) => OnStartCommand(options),
            errors => Task.FromResult(1));

    static async Task<int> OnStartCommand(StartOptions options)
    {
        Stopwatch startupLatencyStopwatch = Stopwatch.StartNew();
        ILoggerFactory loggerFactory = GetLoggerFactory();
        ILogger log = loggerFactory.CreateLogger("Microsoft.DurableTask.Sidecar");

        string listenAddress = $"http://0.0.0.0:{options.ListenPort}";
        log.InitializingSidecar(listenAddress, options.BackendType.ToString());

        IOrchestrationService orchestrationService = GetOrchestrationService(options, loggerFactory);
        await orchestrationService.CreateIfNotExistsAsync();

        // TODO: Support clients that don't share the same runtime type as the service
        IOrchestrationServiceClient orchestrationServiceClient = (IOrchestrationServiceClient)orchestrationService;

        IWebHost host;
        try
        {
            host = new WebHostBuilder()
                .UseKestrel(options =>
                {
                    // Need to force Http2 in Kestrel in unencrypted scenarios
                    // https://docs.microsoft.com/en-us/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0
                    options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
                })
                .UseUrls(listenAddress)
                .ConfigureServices(services =>
                {
                    services.AddGrpc();
                    services.AddSingleton<ILoggerFactory>(loggerFactory);
                    services.AddSingleton<IOrchestrationService>(orchestrationService);
                    services.AddSingleton<IOrchestrationServiceClient>(orchestrationServiceClient);
                    services.AddSingleton<TaskHubGrpcServer>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<TaskHubGrpcServer>();
                    });
                })
                .Build();
            await host.StartAsync();

            log.SidecarInitialized(startupLatencyStopwatch.ElapsedMilliseconds);
        }
        catch (IOException e) when (e.InnerException is AddressInUseException)
        {
            log.SidecarListenPortAlreadyInUse(options.ListenPort);
            return 1;
        }

        if (options.Interactive)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Interactive mode. Type the name of an orchestrator and press [ENTER] to submit. Type 'exit' to quit.");
            Console.WriteLine();
            Console.Write("> ");
            Console.ResetColor();

            // Create a gRPC channel to talk to the management service endpoint that we just started.
            // Alternatively, we could consider making direct calls using TaskHubClient.
            string localListenAddress = $"http://localhost:{options.ListenPort}";
            GrpcChannel grpcChannel = GrpcChannel.ForAddress(localListenAddress, new GrpcChannelOptions
            {
                // NOTE: This is a localhost connection, so we can safely disable TLS.
                UnsafeUseInsecureChannelCallCredentials = true,
            });

            var client = new TaskHubSidecarServiceClient(grpcChannel);

            try
            {
                while (true)
                {
                    string? input = (await ReadLineAsync())?.Trim();
                    if (string.IsNullOrEmpty(input) || string.Equals(input, "help", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("Usage: {orchestrator-name} [{orchestrator-input}]");
                        Console.Write("> ");
                        Console.ResetColor();
                        continue;
                    }

                    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    string[] parts = input.Split(' ');
                    string name = parts.First();

                    var request = new CreateInstanceRequest
                    {
                        Name = name,
                        InstanceId = $"dt-interactive-{Guid.NewGuid():N}",
                    };

                    if (parts.Length > 1)
                    {
                        request.Input = parts[1];
                    }

                    await client.StartInstanceAsync(request);
                }
            }
            finally
            {
                await grpcChannel.ShutdownAsync();
            }
        }
        else
        {
            // TODO: Block until we receive a SIGTERM or SIGKILL
            await Task.Delay(Timeout.Infinite);
        }

        log.SidecarShuttingDown();
        await host.StopAsync();
        host.Dispose();

        return 0;
    }

    static ILoggerFactory GetLoggerFactory() => LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddThh:mm:ss.ffffffZ ";
        });

        // TODO: Support Application Insights URLs for sovereign clouds
        string? appInsightsKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
        if (!string.IsNullOrEmpty(appInsightsKey))
        {
            builder.AddApplicationInsights(appInsightsKey);
        }

        // Support a statically configured logger provider for in-memory testing.
        if (AdditionalLoggerProvider != null)
        {
            builder.AddProvider(AdditionalLoggerProvider);
        }

        // Sidecar logging can be optionally configured using environment variables.
        string? sidecarLogLevelString = Environment.GetEnvironmentVariable("DURABLETASK_SIDECAR_LOGLEVEL");
        if (!Enum.TryParse(sidecarLogLevelString, ignoreCase: true, out LogLevel sidecarLogLevel))
        {
            sidecarLogLevel = LogLevel.Information;
        }

        // Storage provider logs should be warning+ by default and
        // core execution logs (DurableTask.Core) should be information+ by default
        // to support basic tracking.
        builder.AddFilter("DurableTask", LogLevel.Warning);
        builder.AddFilter("DurableTask.Core", LogLevel.Information);
        builder.AddFilter("Microsoft.DurableTask.Sidecar", sidecarLogLevel);

        // ASP.NET Core logs to warning since they can otherwise be noisy.
        // This should be increased if it's necessary to debug gRPC request/response issues.
        builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    });

    static IOrchestrationService GetOrchestrationService(StartOptions options, ILoggerFactory loggerFactory)
    {
        switch (options.BackendType)
        {
            case BackendType.AzureStorage:
                const string AzureStorageConnectionStringName = "DURABLETASK_AZURESTORAGE_CONNECTIONSTRING";
                string? storageConnectionString = Environment.GetEnvironmentVariable(AzureStorageConnectionStringName);
                if (string.IsNullOrEmpty(storageConnectionString))
                {
                    // Local storage emulator: "UseDevelopmentStorage=true"
                    throw new InvalidOperationException($"The Azure Storage provider requires a {AzureStorageConnectionStringName} environment variable.");
                }

                var azureStorageSettings = new AzureStorageOrchestrationServiceSettings
                {
                    TaskHubName = "DurableServerTests",
                    StorageConnectionString = storageConnectionString,
                    MaxQueuePollingInterval = TimeSpan.FromSeconds(5),
                    LoggerFactory = loggerFactory,
                };
                return new AzureStorageOrchestrationService(azureStorageSettings);

            case BackendType.Emulator:
                return SingletonLocalOrchestrationService;

            case BackendType.MSSQL:
                const string SqlConnectionStringName = "DURABLETASK_MSSQL_CONNECTIONSTRING";
                string? sqlConnectionString = Environment.GetEnvironmentVariable(SqlConnectionStringName);
                if (string.IsNullOrEmpty(sqlConnectionString))
                {
                    // Local Windows install: "Server=localhost;Database=DurableDB;Trusted_Connection=True;"
                    throw new InvalidOperationException($"The MSSQL storage provider requires a {SqlConnectionStringName} environment variable.");
                }

                var mssqlSettings = new SqlOrchestrationServiceSettings(sqlConnectionString)
                {
                    LoggerFactory = loggerFactory,
                };
                return new SqlOrchestrationService(mssqlSettings);

            case BackendType.Netherite:
                throw new NotSupportedException("Netherite is not yet supported.");

            default:
                throw new ArgumentException($"Unknown backend type: {options.BackendType}");
        }
    }

    static Task<string?> ReadLineAsync() => InputReader.ReadLineAsync();

    class StandardInputReader : IInputReader
    {
        public Task<string?> ReadLineAsync() => Console.In.ReadLineAsync();
    }
}
