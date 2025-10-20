// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the Durable Task worker.
/// </summary>
public class DurableTaskWorkerOptions
{
    DataConverter dataConverter = JsonDataConverter.Default;

    /// <summary>
    /// Defines the version matching strategy for the Durable Task worker.
    /// </summary>
    public enum VersionMatchStrategy
    {
        /// <summary>
        /// Ignore Orchestration version, all work received is processed.
        /// </summary>
        None = 0,

        /// <summary>
        /// Worker will only process Tasks from Orchestrations with the same version as the worker.
        /// </summary>
        Strict = 1,

        /// <summary>
        /// Worker will process Tasks from Orchestrations whose version is less than or equal to the worker.
        /// </summary>
        CurrentOrOlder = 2,
    }

    /// <summary>
    /// Defines the versioning failure strategy for the Durable Task worker.
    /// </summary>
    public enum VersionFailureStrategy
    {
        /// <summary>
        /// Do not change the orchestration state if the version does not adhere to the matching strategy.
        /// </summary>
        Reject = 0,

        /// <summary>
        /// Fail the orchestration if the version does not adhere to the matching strategy.
        /// </summary>
        Fail = 1,
    }

    /// <summary>
    /// Gets or sets the data converter. Default value is <see cref="JsonDataConverter.Default" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is used for serializing inputs and outputs of <see cref="ITaskOrchestrator" /> and
    /// <see cref="ITaskActivity" />.
    /// </para><para>
    /// When set to <c>null</c>, this will revert to <see cref="JsonDataConverter.Default" />.
    /// </para><para>
    /// WARNING: When changing this value, ensure backwards compatibility is preserved for any in-flight
    /// orchestrations. If it is not, deserialization - and the orchestration - may fail.
    /// </para>
    /// </remarks>
    public DataConverter DataConverter
    {
        get => this.dataConverter;
        set
        {
            if (value is null)
            {
                this.dataConverter = JsonDataConverter.Default;
                this.DataConverterExplicitlySet = false;
            }
            else
            {
                this.dataConverter = value;
                this.DataConverterExplicitlySet = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this client should support entities. If true, all instance ids starting
    /// with '@' are reserved for entities, and validation checks are performed where appropriate.
    /// </summary>
    public bool EnableEntitySupport { get; set; }

    /// <summary>
    /// Gets or sets the maximum timer interval for the
    /// <see cref="TaskOrchestrationContext.CreateTimer(TimeSpan, CancellationToken)"/> method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default maximum timer interval is 3 days. If a
    /// <see cref="TaskOrchestrationContext.CreateTimer(TimeSpan, CancellationToken)"/> call
    /// specifies a 7-day timer, it will be implemented using three separate timers: two for 3 days and one for 1 day.
    /// </para><para>
    /// Long timers are broken up into smaller timers to support certain types of backend storage providers which have
    /// limits on how long a durable timer entity can be created. For example, the Azure Storage state provider uses
    /// scheduled queue messages to implement durable timers, but scheduled queue messages cannot exceed 7 days. In
    /// order to support longer durable timers, a long timer is broken up into smaller timers internally. This division
    /// into multiple timers is not visible to user code. However, it is visible in the generated orchestration
    /// history.
    /// </para><para>
    /// Be aware that changing this setting may be breaking for in-flight orchestrations. For example, if an existing
    /// orchestration has created a timer that exceeds the maximum interval (e.g., a 7 day timer), and this value is
    /// subsequently changed to a higher value (e.g., from 3 days to 10 days) then the next reply of the existing
    /// orchestration will fail with a non-determinism error because the number of intermediate timers scheduled during
    /// the replay (e.g., one timer) will no longer match the number of timers that exist in the orchestration history
    /// (e.g., two timers).
    /// </para><para>
    /// To avoid accidentally breaking existing orchestrations, this value should only be changed for new applications
    /// with no state or when an application is known to have no in-flight orchestration instances.
    /// </para><para>
    /// WARNING: Changing this value from a previously configured value is a breaking change for in-flight
    /// orchestrations.
    /// </para>
    /// </remarks>
    public TimeSpan MaximumTimerInterval { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Gets options for the Durable Task worker concurrency.
    /// </summary>
    /// <remarks>
    /// Worker concurrency options control how many work items of a particular type (e.g., orchestration, activity,
    /// or entity) can be processed concurrently by the worker. It is recommended to set these values based on the
    /// expected workload and the resources available on the machine running the worker.
    /// </remarks>
    public ConcurrencyOptions Concurrency { get; } = new();

    /// <summary>
    /// Gets or sets the versioning options for the Durable Task worker.
    /// </summary>
    /// <remarks>
    /// Worker versioning controls how a worker will handle orchestrations of different versions. Defining both the
    /// version of the worker, the versions that can be worked on, and what to do in case a version does not comply
    /// with the given options.
    /// </remarks>
    public VersioningOptions? Versioning { get; set; }

    /// <summary>
    /// Gets a value indicating whether versioning is explicitly set or not.
    /// </summary>
    public bool IsVersioningSet { get; internal set; }

    /// <summary>
    /// Gets or sets a callback function that determines whether an orchestration should be accepted for work.
    /// </summary>
    [Obsolete("Experimental")]
    public IOrchestrationFilter? OrchestrationFilter { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="DataConverter" /> was explicitly set or not.
    /// </summary>
    /// <remarks>
    /// This value is used to determine if we should resolve <see cref="DataConverter" /> from the
    /// <see cref="IServiceProvider" /> or not. If it is explicitly set (even to the default), we
    /// will <b>not</b> resolve it. If not set, we will attempt to resolve it. This is so the
    /// behavior is consistently irrespective of option configuration ordering.
    /// </remarks>
    internal bool DataConverterExplicitlySet { get; private set; }

    /// <summary>
    /// Applies these option values to another.
    /// </summary>
    /// <param name="other">The other options object to apply to.</param>
    internal void ApplyTo(DurableTaskWorkerOptions other)
    {
        if (other is not null)
        {
            // Make sure to keep this up to date as values are added.
            other.DataConverter = this.DataConverter;
            other.MaximumTimerInterval = this.MaximumTimerInterval;
            other.EnableEntitySupport = this.EnableEntitySupport;
            other.Versioning = this.Versioning;
            other.OrchestrationFilter = this.OrchestrationFilter;
        }
    }

    /// <summary>
    /// Options for the Durable Task worker concurrency.
    /// </summary>
    public class ConcurrencyOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of concurrent activity work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentActivityWorkItems { get; set; } = 100 * Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the maximum number of concurrent orchestration work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentOrchestrationWorkItems { get; set; } = 100 * Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the maximum number of concurrent entity work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentEntityWorkItems { get; set; } = 100 * Environment.ProcessorCount;
    }

    /// <summary>
    /// Options for the Durable Task worker versioning.
    /// </summary>
    public class VersioningOptions
    {
        /// <summary>
        /// Gets or sets the version of orchestrations that the worker can work on.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the default version that will be used for starting new orchestrations.
        /// </summary>
        public string DefaultVersion { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the versioning strategy for the Durable Task worker.
        /// </summary>
        public VersionMatchStrategy MatchStrategy { get; set; } = VersionMatchStrategy.None;

        /// <summary>
        /// Gets or sets the versioning failure strategy for the Durable Task worker.
        /// </summary>
        /// <remarks>
        /// If the version matching strategy is set to <see cref="VersionMatchStrategy.None"/>, this value has no effect.
        /// </remarks>
        public VersionFailureStrategy FailureStrategy { get; set; } = VersionFailureStrategy.Reject;
    }
}
