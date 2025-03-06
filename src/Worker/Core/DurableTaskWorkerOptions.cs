// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the Durable Task worker.
/// </summary>
public class DurableTaskWorkerOptions
{
    DataConverter? dataConverter;
    bool? enableEntitySupport;
    TimeSpan? maximumTimerInterval;

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
        get => this.dataConverter ?? JsonDataConverter.Default;
        set => this.dataConverter = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this client should support entities. If true, all instance ids starting
    /// with '@' are reserved for entities, and validation checks are performed where appropriate.
    /// </summary>
    public bool EnableEntitySupport
    {
        get => this.enableEntitySupport ?? false;
        set => this.enableEntitySupport = value;
    }

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
    public TimeSpan MaximumTimerInterval
    {
        get => this.maximumTimerInterval ?? TimeSpan.FromDays(3);
        set => this.maximumTimerInterval = value;
    }

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
    /// Gets a value indicating whether <see cref="DataConverter" /> was explicitly set or not.
    /// </summary>
    /// <remarks>
    /// This value is used to determine if we should resolve <see cref="DataConverter" /> from the
    /// <see cref="IServiceProvider" /> or not. If it is explicitly set (even to the default), we
    /// will <b>not</b> resolve it. If not set, we will attempt to resolve it. This is so the
    /// behavior is consistently irrespective of option configuration ordering.
    /// </remarks>
    internal bool DataConverterExplicitlySet => this.dataConverter is not null;

    /// <summary>
    /// Applies these option values to another.
    /// </summary>
    /// <param name="other">The other options object to apply to.</param>
    internal void ApplyTo(DurableTaskWorkerOptions other)
    {
        if (other is not null)
        {
            // Make sure to keep this up to date as values are added.
            ApplyIfSet(this.dataConverter, ref other.dataConverter);
            ApplyIfSet(this.enableEntitySupport, ref other.enableEntitySupport);
            ApplyIfSet(this.maximumTimerInterval, ref other.maximumTimerInterval);
            this.Concurrency.ApplyTo(other.Concurrency);
        }
    }

    static void ApplyIfSet<T>(T? value, ref T? target)
    {
        if (value is not null && target is null)
        {
            target = value;
        }
    }

    /// <summary>
    /// Options for the Durable Task worker concurrency.
    /// </summary>
    public class ConcurrencyOptions
    {
        static readonly int DefaultMaxConcurrency = 100 * Environment.ProcessorCount;

        int? maxActivity;
        int? maxOrchestration;
        int? maxEntity;

        /// <summary>
        /// Gets or sets the maximum number of concurrent activity work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentActivityWorkItems
        {
            get => this.maxActivity ?? DefaultMaxConcurrency;
            set => this.maxActivity = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of concurrent orchestration work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentOrchestrationWorkItems
        {
            get => this.maxOrchestration ?? DefaultMaxConcurrency;
            set => this.maxOrchestration = value;
        }

        /// <summary>
        /// Gets or sets the maximum number of concurrent entity work items that can be processed by the worker.
        /// </summary>
        public int MaximumConcurrentEntityWorkItems
        {
            get => this.maxEntity ?? DefaultMaxConcurrency;
            set => this.maxEntity = value;
        }

        /// <summary>
        /// Applies these option values to another.
        /// </summary>
        /// <param name="other">The options to apply this options values to.</param>
        internal void ApplyTo(ConcurrencyOptions other)
        {
            if (other is not null)
            {
                ApplyIfSet(this.maxActivity, ref other.maxActivity);
                ApplyIfSet(this.maxOrchestration, ref other.maxOrchestration);
                ApplyIfSet(this.maxEntity, ref other.maxEntity);
            }
        }
    }
}
