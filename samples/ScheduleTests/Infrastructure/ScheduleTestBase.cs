using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.ScheduledTasks;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ScheduleTests.Infrastructure
{
    public abstract class ScheduleTestBase : IAsyncLifetime
    {
        protected IHost Host { get; private set; }
        protected DurableTaskClient Client { get; private set; }
        protected ILogger Logger { get; private set; }

        protected ScheduleTestBase()
        {
            var builder = Host.CreateApplicationBuilder();

            // Get configuration
            string connectionString = builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Missing required configuration 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'");

            // Configure the worker
            builder.Services.AddDurableTaskWorker(builder =>
            {
                builder.AddTasks(r => r.AddAllGeneratedTasks());
                builder.UseDurableTaskScheduler(connectionString);
                builder.UseScheduledTasks();
            });

            // Configure the client
            builder.Services.AddDurableTaskClient(builder =>
            {
                builder.UseDurableTaskScheduler(connectionString);
                builder.UseScheduledTasks();
            });

            // Configure logging
            builder.Services.AddLogging(logging =>
            {
                logging.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.UseUtcTimestamp = true;
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
                });
            });

            Host = builder.Build();
        }

        public async Task InitializeAsync()
        {
            await Host.StartAsync();
            Client = Host.Services.GetRequiredService<DurableTaskClient>();
            Logger = Host.Services.GetRequiredService<ILogger<ScheduleTestBase>>();
        }

        public async Task DisposeAsync()
        {
            if (Host != null)
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
        }

        protected async Task WaitForOrchestrationCompletion(string instanceId, TimeSpan timeout)
        {
            try
            {
                await Client.WaitForOrchestrationToReachStatus(
                    instanceId,
                    OrchestrationRuntimeStatus.Completed,
                    timeout);
            }
            catch (TimeoutException)
            {
                var status = await Client.GetInstanceAsync(instanceId);
                throw new TimeoutException(
                    $"Orchestration {instanceId} did not complete within {timeout}. Current status: {status?.RuntimeStatus}");
            }
        }
    }
} 