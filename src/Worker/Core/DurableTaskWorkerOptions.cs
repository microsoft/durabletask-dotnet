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
        }
    }
}
