// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Sidecar.Dispatcher;

class TaskHubDispatcherHost
{
    readonly TaskOrchestrationDispatcher orchestrationDispatcher;
    readonly TaskActivityDispatcher activityDispatcher;

    readonly IOrchestrationService orchestrationService;
    readonly ILogger log;

    public TaskHubDispatcherHost(
        ILoggerFactory loggerFactory,
        ITrafficSignal trafficSignal,
        IOrchestrationService orchestrationService,
        ITaskExecutor taskExecutor)
    {
        this.orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        this.log = loggerFactory.CreateLogger("Microsoft.DurableTask.Sidecar");

        this.orchestrationDispatcher = new TaskOrchestrationDispatcher(this.log, trafficSignal, orchestrationService, taskExecutor);
        this.activityDispatcher = new TaskActivityDispatcher(this.log, trafficSignal, orchestrationService, taskExecutor);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Start any background processing in the orchestration service
        await this.orchestrationService.StartAsync();

        // Start the dispatchers, which will allow orchestrations/activities to execute
        await Task.WhenAll(
            this.orchestrationDispatcher.StartAsync(cancellationToken),
            this.activityDispatcher.StartAsync(cancellationToken));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the dispatchers from polling the orchestration service
        await Task.WhenAll(
            this.orchestrationDispatcher.StopAsync(cancellationToken),
            this.activityDispatcher.StopAsync(cancellationToken));

        // Tell the storage provider to stop doing any background work.
        await this.orchestrationService.StopAsync();
    }
}

