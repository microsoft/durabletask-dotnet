// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Core = DurableTask.Core;
using CoreOrchestrationQuery = DurableTask.Core.Query.OrchestrationQuery;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim;

/// <summary>
/// A shim client for interacting with the backend via <see cref="Core.IOrchestrationServiceClient" />.
/// </summary>
class ShimDurableTaskClient : DurableTaskClient
{
    readonly ShimDurableTaskClientOptions options;
    ShimDurableEntityClient? entities;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of this client.</param>
    /// <param name="options">The client options.</param>
    [ActivatorUtilitiesConstructor]
    public ShimDurableTaskClient(
        string name, IOptionsMonitor<ShimDurableTaskClientOptions> options)
        : this(name, Check.NotNull(options).Get(name))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The client options.</param>
    public ShimDurableTaskClient(string name, ShimDurableTaskClientOptions options)
        : base(name)
    {
        this.options = Check.NotNull(options);
    }

    /// <inheritdoc/>
    public override DurableEntityClient Entities
    {
        get
        {
            if (this.entities is null)
            {
                if (this.options.Client is not IEntityOrchestrationService entityService)
                {
                    throw new NotSupportedException(
                        "The configured IOrchestrationServiceClient does not support entities.");
                }

                this.entities = new(this.Name, this.options.Client, entityService, this.options.DataConverter);
            }

            return this.entities;
        }
    }

    DataConverter DataConverter => this.options.DataConverter;

    IOrchestrationServiceClient Client => this.options.Client!;

    IOrchestrationServicePurgeClient PurgeClient => this.CastClient<IOrchestrationServicePurgeClient>();

    /// <inheritdoc/>
    public override ValueTask DisposeAsync() => default;

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        IList<Core.OrchestrationState> states = await this.Client.GetOrchestrationStateAsync(instanceId, false);
        if (states is null or { Count: 0 })
        {
            return null;
        }

        return this.ToMetadata(states.First(), getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? query = null)
    {
        // Get this early to force an exception if not supported.
        IOrchestrationServiceQueryClient queryClient = this.CastClient<IOrchestrationServiceQueryClient>();
        return Pageable.Create(async (continuation, pageSize, cancellation) =>
        {
            CoreOrchestrationQuery coreQuery = new()
            {
                RuntimeStatus = query?.Statuses?.Select(x => x.ConvertToCore()).ToList(),
                CreatedTimeFrom = query?.CreatedFrom?.UtcDateTime,
                CreatedTimeTo = query?.CreatedTo?.UtcDateTime,
                TaskHubNames = query?.TaskHubNames?.ToList(),
                PageSize = pageSize ?? query?.PageSize ?? OrchestrationQuery.DefaultPageSize,
                ContinuationToken = continuation ?? query?.ContinuationToken,
                InstanceIdPrefix = query?.InstanceIdPrefix,
                FetchInputsAndOutputs = query?.FetchInputsAndOutputs ?? false,
            };

            OrchestrationQueryResult result = await queryClient.GetOrchestrationWithQueryAsync(
                coreQuery, cancellation);

            var metadata = result.OrchestrationState.Select(x => this.ToMetadata(x, coreQuery.FetchInputsAndOutputs))
                .ToList();
            return new Page<OrchestrationMetadata>(metadata, result.ContinuationToken);
        });
    }

    /// <inheritdoc/>
    public override async Task<PurgeResult> PurgeInstanceAsync(
        string instanceId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        cancellation.ThrowIfCancellationRequested();
        Core.PurgeResult result = await this.PurgeClient.PurgeInstanceStateAsync(instanceId);
        return result.ConvertFromCore();
    }

    /// <inheritdoc/>
    public override async Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, CancellationToken cancellation = default)
    {
        Check.NotNull(filter);
        cancellation.ThrowIfCancellationRequested();
        Core.PurgeResult result = await this.PurgeClient.PurgeInstanceStateAsync(filter.ConvertToCore());
        return result.ConvertFromCore();
    }

    /// <inheritdoc/>
    public override Task RaiseEventAsync(
        string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        Check.NotNullOrEmpty(eventName);

        string? serializedInput = this.DataConverter.Serialize(eventPayload);
        return this.SendInstanceMessageAsync(
            instanceId, new EventRaisedEvent(-1, serializedInput) { Name = eventName }, cancellation);
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        string instanceId = options?.InstanceId ?? Guid.NewGuid().ToString("N");
        OrchestrationInstance instance = new()
        {
            InstanceId = instanceId,
            ExecutionId = Guid.NewGuid().ToString("N"),
        };

        string? serializedInput = this.DataConverter.Serialize(input);
        TaskMessage message = new()
        {
            OrchestrationInstance = instance,
            Event = new ExecutionStartedEvent(-1, serializedInput)
            {
                Name = orchestratorName.Name,
                Version = orchestratorName.Version,
                OrchestrationInstance = instance,
                ScheduledStartTime = options?.StartAt?.UtcDateTime,
            },
        };

        await this.Client.CreateTaskOrchestrationAsync(message);
        return instanceId;
    }

    /// <inheritdoc/>
    public override Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
        => this.SendInstanceMessageAsync(instanceId, new ExecutionSuspendedEvent(-1, reason), cancellation);

    /// <inheritdoc/>
    public override Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
        => this.SendInstanceMessageAsync(instanceId, new ExecutionResumedEvent(-1, reason), cancellation);

    /// <inheritdoc/>
    public override Task TerminateInstanceAsync(
        string instanceId, object? output = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        cancellation.ThrowIfCancellationRequested();
        string? reason = this.DataConverter.Serialize(output);
        return this.Client.ForceTerminateTaskOrchestrationAsync(instanceId, reason);
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        OrchestrationState state = await this.Client.WaitForOrchestrationAsync(
            instanceId, null, TimeSpan.MaxValue, cancellation);
        return this.ToMetadata(state, getInputsAndOutputs);
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);

        while (true)
        {
            OrchestrationMetadata? metadata = await this.GetInstancesAsync(
                instanceId, getInputsAndOutputs, cancellation);
            if (metadata is null)
            {
                throw new InvalidOperationException($"Orchestration with instanceId '{instanceId}' does not exist");
            }

            if (metadata.RuntimeStatus != OrchestrationRuntimeStatus.Pending)
            {
                // TODO: Evaluate what to do with "Suspended" state. Do we wait on that?
                return metadata;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
        }
    }

    [return: NotNullIfNotNull("state")]
    OrchestrationMetadata? ToMetadata(Core.OrchestrationState? state, bool getInputsAndOutputs)
    {
        if (state is null)
        {
            return null;
        }

        return new OrchestrationMetadata(state.Name, state.OrchestrationInstance.InstanceId)
        {
            DataConverter = getInputsAndOutputs ? this.DataConverter : null,
            RuntimeStatus = state.OrchestrationStatus.ConvertFromCore(),
            CreatedAt = state.CreatedTime,
            LastUpdatedAt = state.LastUpdatedTime,
            SerializedInput = state.Input,
            SerializedOutput = state.Output,
            SerializedCustomStatus = state.Status,
            FailureDetails = state.FailureDetails?.ConvertFromCore(),
        };
    }

    T CastClient<T>()
    {
        if (this.Client is T t)
        {
            return t;
        }

        throw new NotSupportedException($"Provided IOrchestrationServiceClient does not implement {typeof(T)}.");
    }

    Task SendInstanceMessageAsync(string instanceId, HistoryEvent @event, CancellationToken cancellation)
    {
        Check.NotNullOrEmpty(instanceId);
        Check.NotNull(@event);

        cancellation.ThrowIfCancellationRequested();

        TaskMessage message = new()
        {
            OrchestrationInstance = new() { InstanceId = instanceId },
            Event = @event,
        };

        return this.Client.SendTaskOrchestrationMessageAsync(message);
    }
}
