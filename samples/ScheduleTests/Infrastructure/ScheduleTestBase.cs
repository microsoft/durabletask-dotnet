// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScheduleTests.Tasks;
using Xunit;

namespace ScheduleTests.Infrastructure
{
    public abstract class ScheduleTestBase : IAsyncLifetime
    {
        readonly IHost? host;
        protected DurableTaskClient Client { get; private set; } = null!;
        protected ScheduledTaskClient ScheduledTaskClient { get; private set; } = null!;
        protected ILogger Logger { get; private set; } = null!;

        protected ScheduleTestBase()
        {
            var builder = new HostBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory)
                          .AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Get configuration
                    string connectionString = hostContext.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
                        ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

                    // Configure the worker
                    services.AddDurableTaskWorker(builder =>
                    {
                        // Register our test tasks
                        builder.AddTasks(r =>
                        {
                            r.AddOrchestrator(typeof(SimpleOrchestrator));
                            r.AddOrchestrator(typeof(LongRunningOrchestrator));
                            r.AddOrchestrator(typeof(RandomRunTimeOrchestrator));
                            r.AddActivity(typeof(TestActivity));
                        });
                        builder.UseDurableTaskScheduler(connectionString);
                        builder.UseScheduledTasks();
                    });

                    // Configure the client
                    services.AddDurableTaskClient(builder =>
                    {
                        builder.UseDurableTaskScheduler(connectionString);
                        builder.UseScheduledTasks();
                    });

                    // Configure logging
                    services.AddLogging(logging =>
                    {
                        logging.AddSimpleConsole(options =>
                        {
                            options.SingleLine = true;
                            options.UseUtcTimestamp = true;
                            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
                        });
                    });

                    // configure ilogger not iloggerfactory to log to console
                    services.AddSingleton<ILogger>(sp =>
                    {
                        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                        return loggerFactory.CreateLogger<ScheduleTestBase>();
                    });
                });

            this.host = builder.Build();
        }

        public async Task InitializeAsync()
        {
            if (this.host == null) throw new InvalidOperationException("Host was not properly initialized");
            await this.host.StartAsync();
            this.Client = this.host.Services.GetRequiredService<DurableTaskClient>();
            this.ScheduledTaskClient = this.host.Services.GetRequiredService<ScheduledTaskClient>();
            this.Logger = this.host.Services.GetRequiredService<ILogger<ScheduleTestBase>>();

            // delete all schedules
            await foreach (var schedule in this.ScheduledTaskClient.ListSchedulesAsync())
            {
                // get schedule client
                var scheduleClient = this.ScheduledTaskClient.GetScheduleClient(schedule.ScheduleId);
                await scheduleClient.DeleteAsync();
            }
        }

        public async Task DisposeAsync()
        {
            if (this.host != null)
            {
                await this.host.StopAsync();
                if (this.host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (this.host is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}