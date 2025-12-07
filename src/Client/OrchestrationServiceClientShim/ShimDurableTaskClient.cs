// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Core = DurableTask.Core;
using CoreOrchestrationQuery = DurableTask.Core.Query.OrchestrationQuery;

namespace Microsoft.DurableTask.Client.OrchestrationServiceClientShim;

/// <summary>
/// A shim client for interacting with the backend via <see cref="Core.IOrchestrationServiceClient" />.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ShimDurableTaskClient"/> class.
/// </remarks>
/// <param name="name">The name of the client.</param>
/// <param name="options">The client options.</param>
class ShimDurableTaskClient(string name, ShimDurableTaskClientOptions options) : DurableTaskClient(name)
{
    readonly ShimDurableTaskClientOptions options = Check.NotNull(options);
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

    /// <inheritdoc/>
    public override DurableEntityClient Entities
    {
        get
        {
            if (!this.options.EnableEntitySupport)
            {
                throw new InvalidOperationException("Entity support is not enabled.");
            }

            if (this.entities is null)
            {
                if (this.options.Entities.Queries is null)
                {
                    throw new NotSupportedException(
                        "The configured IOrchestrationServiceClient does not support entities.");
                }

                this.entities = new(this.Name, this.options);
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
        string instanceId, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        cancellation.ThrowIfCancellationRequested();

        // TODO: Support recursive purge of sub-orchestrations
        Core.PurgeResult result = await this.PurgeClient.PurgeInstanceStateAsync(instanceId);
        return result.ConvertFromCore();
    }

    /// <inheritdoc/>
    public override async Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, PurgeInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        Check.NotNull(filter);
        cancellation.ThrowIfCancellationRequested();

        // TODO: Support recursive purge of sub-orchestrations
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

        var tags = new Dictionary<string, string>();
        if (options?.Tags != null)
        {
            tags = options.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        tags[OrchestrationTags.CreateTraceForNewOrchestration] = "true";
        tags[OrchestrationTags.RequestTime] = DateTimeOffset.UtcNow.ToString(CultureInfo.InvariantCulture);

        TaskMessage message = new()
        {
            OrchestrationInstance = instance,
            Event = new ExecutionStartedEvent(-1, serializedInput)
            {
                Name = orchestratorName.Name,
                Version = options?.Version ?? string.Empty,
                OrchestrationInstance = instance,
                ScheduledStartTime = options?.StartAt?.UtcDateTime,
                ParentTraceContext = Activity.Current is { } activity ? new Core.Tracing.DistributedTraceContext(activity.Id!, activity.TraceStateString) : null,
                Tags = tags,
            },
        };

        Core.OrchestrationStatus[]? dedupeStatuses = null;
        if (options?.DedupeStatuses != null && options.DedupeStatuses.Count > 0)
        {
            dedupeStatuses = options.DedupeStatuses
                .Select(s =>
                {
                    if (!Enum.TryParse<OrchestrationRuntimeStatus>(s, ignoreCase: true, out var status))
                    {
                        var validStatuses = string.Join(", ", StartOrchestrationOptionsExtensions.ValidDedupeStatuses.Select(ts => ts.ToString()));
                        throw new ArgumentException(
                            $"Invalid orchestration runtime status for deduplication: '{s}'. Valid statuses for deduplication are: {validStatuses}",
                            nameof(options.DedupeStatuses));
                    }
                    return status.ConvertToCore();
                })
                .ToArray();
        }

        await this.Client.CreateTaskOrchestrationAsync(message, dedupeStatuses);
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
        string instanceId, TerminateInstanceOptions? options = null, CancellationToken cancellation = default)
    {
        object? output = options?.Output;
        Check.NotNullOrEmpty(instanceId);
        cancellation.ThrowIfCancellationRequested();
        string? reason = this.DataConverter.Serialize(output);

        // TODO: Support recursive termination of sub-orchestrations
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

    /// <inheritdoc/>
    public override async Task<string> RestartAsync(
        string instanceId,
        bool restartWithNewInstanceId = false,
        CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        cancellation.ThrowIfCancellationRequested();

        // Get the current orchestration status to retrieve the name and input
        OrchestrationMetadata? status = await this.GetInstanceAsync(instanceId, getInputsAndOutputs: true, cancellation);

        if (status == null)
        {
            throw new ArgumentException($"An orchestration with the instanceId {instanceId} was not found.");
        }

        bool isInstaceNotCompleted = status.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
                                    status.RuntimeStatus == OrchestrationRuntimeStatus.Pending ||
                                    status.RuntimeStatus == OrchestrationRuntimeStatus.Suspended;

        if (isInstaceNotCompleted && !restartWithNewInstanceId)
        {
            throw new InvalidOperationException($"Instance '{instanceId}' cannot be restarted while it is in state '{status.RuntimeStatus}'. " +
                   "Wait until it has completed, or restart with a new instance ID.");
        }

        // Determine the instance ID for the restarted orchestration
        string newInstanceId = restartWithNewInstanceId ? Guid.NewGuid().ToString("N") : instanceId;

        OrchestrationInstance instance = new()
        {
            InstanceId = newInstanceId,
            ExecutionId = Guid.NewGuid().ToString("N"),
        };

        // Use the original serialized input directly to avoid double serialization
        // TODO: OrchestrationMetada doesn't have version property so we don't support version here.
        // Issue link: https://github.com/microsoft/durabletask-dotnet/issues/463
        TaskMessage message = new()
        {
            OrchestrationInstance = instance,
            Event = new ExecutionStartedEvent(-1, status.SerializedInput)
            {
                Name = status.Name,
                OrchestrationInstance = instance,
            },
        };

        await this.Client.CreateTaskOrchestrationAsync(message);
        return newInstanceId;
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
