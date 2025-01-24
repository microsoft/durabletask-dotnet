using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

public class ScheduleEntity : TaskEntity
{
    private readonly ILogger logger;
    private ScheduleState currentState;
    private int runScheduleCount = 0; // Track how many times RunSchedule has been called

    public ScheduleEntity(ILogger logger)
    {
        this.logger = logger;
        this.currentState = ScheduleState.Provisioning; // Initial state
    }

    /// <summary>
    /// Creates a new schedule.
    /// </summary>
    public async Task CreateSchedule(ScheduleCreationDetails details)
    {
        if (this.currentState != ScheduleState.Provisioning)
        {
            throw new InvalidOperationException("Schedule is already created.");
        }

        // Validate input
        if (details == null)
        {
            throw new ArgumentNullException(nameof(details));
        }

        // Simulate schedule creation (e.g., save to database)
        logger.LogInformation($"Creating schedule with details: {details}");

        // Transition to Active state
        this.currentState = ScheduleState.Active;

        // Call RunSchedule at the end of CreateSchedule
        await RunSchedule();
    }

    /// <summary>
    /// Updates an existing schedule.
    /// </summary>
    public async Task UpdateSchedule(ScheduleUpdateDetails details)
    {
        if (this.currentState == ScheduleState.Provisioning)
        {
            throw new InvalidOperationException("Cannot update a schedule that is still provisioning.");
        }

        if (this.currentState != ScheduleState.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active state to update.");
        }

        // Validate input
        if (details == null)
        {
            throw new ArgumentNullException(nameof(details));
        }

        // Transition to Updating state
        this.currentState = ScheduleState.Updating;

        try
        {
            // Simulate schedule update (e.g., save to database)
            logger.LogInformation($"Updating schedule with details: {details}");

            // Transition back to Active state
            this.currentState = ScheduleState.Active;
        }
        catch (Exception ex)
        {
            // Transition to Failed state if update fails
            this.currentState = ScheduleState.Failed;
            logger.LogError(ex, "Failed to update schedule.");
            throw;
        }
    }

    /// <summary>
    /// Pauses the schedule.
    /// </summary>
    public void PauseSchedule()
    {
        if (this.currentState != ScheduleState.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active state to pause.");
        }

        // Transition to Paused state
        this.currentState = ScheduleState.Paused;
        logger.LogInformation("Schedule paused.");
    }

    /// <summary>
    /// Resumes the schedule.
    /// </summary>
    public void ResumeSchedule()
    {
        if (this.currentState != ScheduleState.Paused)
        {
            throw new InvalidOperationException("Schedule must be in Paused state to resume.");
        }

        // Transition to Active state
        this.currentState = ScheduleState.Active;
        logger.LogInformation("Schedule resumed.");
    }

    /// <summary>
    /// Deletes the schedule.
    /// </summary>
    public void DeleteSchedule()
    {
        // Simulate schedule deletion (e.g., remove from database)
        logger.LogInformation("Schedule deleted.");

        // Reset state
        this.currentState = ScheduleState.Provisioning;
        this.runScheduleCount = 0;
    }

    /// <summary>
    /// Runs the schedule.
    /// </summary>
    private async Task RunSchedule()
    {
        if (this.runScheduleCount > 0)
        {
            throw new InvalidOperationException("RunSchedule can only be called once.");
        }

        if (this.currentState != ScheduleState.Active)
        {
            throw new InvalidOperationException("Schedule must be in Active state to run.");
        }

        // Simulate schedule execution
        logger.LogInformation("Running schedule.");
        this.runScheduleCount++;

        // Simulate a long-running task
        await Task.Delay(1000); // Replace with actual schedule logic
    }

    /// <summary>
    /// Gets the current state of the schedule.
    /// </summary>
    public ScheduleState GetCurrentState()
    {
        return this.currentState;
    }
}


public class ScheduleCreationDetails
{
    public string Name { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string CronExpression { get; set; }
}

public class ScheduleUpdateDetails
{
    public DateTime? NewStartTime { get; set; }
    public DateTime? NewEndTime { get; set; }
    public string NewCronExpression { get; set; }
}