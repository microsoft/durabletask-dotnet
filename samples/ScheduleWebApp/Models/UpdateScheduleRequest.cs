// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace ScheduleWebApp.Models;

/// <summary>
/// Represents a request to update an existing schedule.
/// </summary>
public class UpdateScheduleRequest
{
    /// <summary>
    /// Gets or sets the name of the orchestration to be scheduled.
    /// </summary>
    public string OrchestrationName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the input data for the orchestration.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the time interval between schedule executions.
    /// </summary>
    public TimeSpan Interval { get; set; }

    /// <summary>
    /// Gets or sets the time when the schedule should start.
    /// </summary>
    public DateTimeOffset? StartAt { get; set; }

    /// <summary>
    /// Gets or sets the time when the schedule should end.
    /// </summary>
    public DateTimeOffset? EndAt { get; set; }
}
