// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
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
    // Polling parameters for WaitForInstanceStartAsync. PollingInterval matches the historical fixed
    // 1-second polling cadence and is used, unjittered, for every steady-state delay -- so long-run
    // polling volume never exceeds the historical rate. To desynchronize concurrent callers (avoiding
    // synchronized polling bursts against the backend) without inflating that steady-state volume, a
    // randomized *initial phase offset* -- uniformly distributed in [0, PollingInterval) -- is applied
    // exactly once, before the first delay of a given WaitForInstanceStartAsync call. This is a one-time
    // cost per call (not repeated per iteration), so it does not change the long-run polling rate, and
    // it still never exceeds the historical 1-second worst-case detection latency.
    static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

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
        OrchestrationMetadata? orchestrationState = await this.GetInstanceAsync(instanceId, cancellation);

        // The orchestration doesn't exist, nothing to purge
        if (orchestrationState == null)
        {
            return new PurgeResult(0);
        }

        bool isEntity = this.options.EnableEntitySupport && instanceId[0] == '@';
        if (!isEntity && !orchestrationState.IsCompleted)
        {
            throw new InvalidOperationException($"Only orchestrations in a terminal state can be purged, " +
                $"but the orchestration with instance ID {instanceId} has status {orchestrationState.RuntimeStatus}");
        }

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
    // This implementation treats a null dedupe statuses field as all statuses being reusable.
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
                        throw new ArgumentException(
                            $"Invalid orchestration runtime status: '{s}' for deduplication.");
                    }
                    return status.ConvertToCore();
                })
                .ToArray();
        }

        await this.TerminateTaskOrchestrationWithReusableRunningStatusAndWaitAsync(instanceId, dedupeStatuses, cancellation);
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

        // A one-time randomized phase offset (see ComputeNextPollingDelay) is applied only to the first
        // delay of this call so concurrent waiters desynchronize without increasing steady-state
        // polling volume beyond the historical fixed 1-second cadence.
        bool isInitialDelay = true;
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

            // The first delay is a randomized phase offset (bounded by the historical 1-second
            // cadence) that desynchronizes concurrent waiters; every delay after that is the fixed
            // historical 1-second interval, unjittered, so steady-state polling volume never exceeds
            // the historical rate. Either way, the delay never exceeds 1 second, preserving prompt-start
            // observation.
            TimeSpan delay = ComputeNextPollingDelay(isInitialDelay);
            isInitialDelay = false;
            await this.DelayAsync(delay, cancellation);
        }
    }

    /// <inheritdoc/>
    // This implementation will terminate an existing non-terminal instance if restartWithNewInstanceId
    // is false, and wait for the existing instance to enter a terminal state before restarting it until
    // the cancellation token is cancelled.
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

        if (!restartWithNewInstanceId)
        {
            await this.TerminateTaskOrchestrationWithReusableRunningStatusAndWaitAsync(
                instanceId,
                dedupeStatuses: null,
                cancellation,
                existingOrchestration: status);
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

        await this.Client.CreateTaskOrchestrationAsync(message, dedupeStatuses: null);
        return newInstanceId;
    }

    /// <summary>
    /// Computes the delay to wait before the next <see cref="WaitForInstanceStartAsync"/> polling attempt.
    /// </summary>
    /// <param name="isInitialDelay">
    /// <see langword="true"/> if this is the first delay computed for a given <see
    /// cref="WaitForInstanceStartAsync"/> call; <see langword="false"/> for every subsequent delay in
    /// that call.
    /// </param>
    /// <returns>
    /// When <paramref name="isInitialDelay"/> is <see langword="true"/>, a one-time randomized phase
    /// offset uniformly distributed in [<see cref="TimeSpan.Zero"/>, <see cref="PollingInterval"/>) that
    /// desynchronizes concurrent callers. Otherwise, the fixed <see cref="PollingInterval"/> (1 second),
    /// unjittered, so steady-state polling volume never exceeds the historical rate. In both cases the
    /// returned delay never exceeds <see cref="PollingInterval"/>, preserving the historical worst-case
    /// detection latency for <see cref="WaitForInstanceStartAsync"/>.
    /// </returns>
    internal static TimeSpan ComputeNextPollingDelay(bool isInitialDelay)
    {
        if (isInitialDelay)
        {
            return TimeSpan.FromMilliseconds(PollingInterval.TotalMilliseconds * PollingJitter.NextDouble());
        }

        return PollingInterval;
    }

    /// <summary>
    /// Awaits the delay between <see cref="WaitForInstanceStartAsync"/> polling attempts.
    /// </summary>
    /// <remarks>
    /// This is factored out from a direct <see cref="Task.Delay(TimeSpan, CancellationToken)"/> call
    /// purely as an internal seam: it lets tests deterministically observe (and coordinate around) the
    /// moment a polling delay begins -- e.g. to cancel only once the delay is genuinely in progress --
    /// without relying on wall-clock timing assumptions. It does not change production behavior.
    /// </remarks>
    /// <param name="delay">The delay to await.</param>
    /// <param name="cancellation">The cancellation token to honor while awaiting the delay.</param>
    /// <returns>A task that completes after the delay elapses, or is cancelled via <paramref name="cancellation"/>.</returns>
    internal virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellation) => Task.Delay(delay, cancellation);

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

    async Task TerminateTaskOrchestrationWithReusableRunningStatusAndWaitAsync(
            string instanceId,
            OrchestrationStatus[]? dedupeStatuses,
            CancellationToken cancellation,
            OrchestrationMetadata? existingOrchestration = null)
    {
        var runningStatuses = new List<OrchestrationStatus>()
            {
                OrchestrationStatus.Running,
                OrchestrationStatus.Pending,
                OrchestrationStatus.Suspended,
            };

        if (dedupeStatuses != null && runningStatuses.Any(
            status => !dedupeStatuses.Contains(status)) && dedupeStatuses.Contains(OrchestrationStatus.Terminated))
        {
            throw new ArgumentException(
                "Invalid dedupe statuses: cannot include 'Terminated' while also allowing reuse of running instances, " +
                "because the running instance would be terminated and then immediately conflict with the dedupe check.");
        }

        // At least one running status is reusable, so determine if an orchestration already exists with this status and terminate it if so
        if (dedupeStatuses == null || runningStatuses.Any(status => !dedupeStatuses.Contains(status)))
        {
            OrchestrationMetadata? metadata = existingOrchestration ?? await this.GetInstancesAsync(instanceId, getInputsAndOutputs: false, cancellation);

            if (metadata != null)
            {
                OrchestrationStatus orchestrationStatus = metadata.RuntimeStatus.ConvertToCore();
                if (dedupeStatuses?.Contains(orchestrationStatus) == true)
                {
                    throw new OrchestrationAlreadyExistsException($"An orchestration with instance ID '{instanceId}' and status " +
                        $"'{metadata.RuntimeStatus}' already exists");
                }

                if (runningStatuses.Contains(orchestrationStatus))
                {
                    // Check for cancellation before attempting to terminate the orchestration
                    cancellation.ThrowIfCancellationRequested();

                    string dedupeStatusesDescription = dedupeStatuses == null
                        ? "null (all statuses reusable)"
                        : dedupeStatuses.Length == 0
                            ? "[] (all statuses reusable)"
                            : $"[{string.Join(", ", dedupeStatuses)}]";

                    string terminationReason = $"A new instance creation request has been issued for instance {instanceId} which " +
                        $"currently has status {metadata.RuntimeStatus}. Since the dedupe statuses of the creation request, " +
                        $"{dedupeStatusesDescription}, do not contain the orchestration's status, the orchestration has been terminated " +
                        $"and a new instance with the same instance ID will be created.";

                    await this.TerminateInstanceAsync(instanceId, terminationReason, cancellation);

                    await this.WaitForInstanceCompletionAsync(instanceId, cancellation: cancellation);
                }
            }
        }
    }

    /// <summary>
    /// A minimal thread-safe random source used to jitter <see cref="WaitForInstanceStartAsync"/> polling
    /// delays across concurrent callers. <see cref="System.Random"/> is not thread-safe, and its
    /// parameterless constructor can produce correlated sequences when many instances are created around
    /// the same tick -- which is exactly the kind of synchronized behavior this jitter is meant to avoid.
    /// A single, securely-seeded instance guarded by a lock avoids both issues.
    /// </summary>
    static class PollingJitter
    {
        static readonly object SyncRoot = new();
        static readonly Random Shared = CreateSeededRandom();

        /// <summary>
        /// Returns a thread-safe random double in the range [0.0, 1.0).
        /// </summary>
        /// <returns>A random double in the range [0.0, 1.0).</returns>
        public static double NextDouble()
        {
            lock (SyncRoot)
            {
                return Shared.NextDouble();
            }
        }

        static Random CreateSeededRandom()
        {
            byte[] seedBytes = new byte[sizeof(int)];
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            rng.GetBytes(seedBytes);
            return new Random(BitConverter.ToInt32(seedBytes, 0));
        }
    }
}
