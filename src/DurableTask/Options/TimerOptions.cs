// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Options;

/// <summary>
/// Configuration options that impact the behavior of Durable timers.
/// </summary>
public class TimerOptions
{
    internal static TimerOptions Default { get; set; } = new();

    /// <summary>
    /// Sets the maximum timer interval for the
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
    /// into multiple timers is not visible to user code. However, it is visible in the generated orchestration history.
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
    /// </para>
    /// </remarks>
    // WARNING: Changing this default value is a breaking change for in-flight orchestrations!
    public TimeSpan MaximumTimerInterval { get; set; } = TimeSpan.FromDays(3);
}
