// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.ScheduledTasks;
using ScheduleWebApp.Models;

namespace ScheduleWebApp.Controllers;

/// <summary>
/// Controller for managing scheduled tasks through a REST API.
/// Provides endpoints for creating, reading, updating, and deleting schedules,
/// as well as pausing and resuming them.
/// </summary>
[ApiController]
[Route("schedules")]
public class ScheduleController : ControllerBase
{
    readonly ScheduledTaskClient scheduledTaskClient;
    readonly ILogger<ScheduleController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleController"/> class.
    /// </summary>
    /// <param name="scheduledTaskClient">Client for managing scheduled tasks.</param>
    /// <param name="logger">Logger for recording controller operations.</param>
    public ScheduleController(
        ScheduledTaskClient scheduledTaskClient,
        ILogger<ScheduleController> logger)
    {
        this.scheduledTaskClient = scheduledTaskClient ?? throw new ArgumentNullException(nameof(scheduledTaskClient));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new schedule based on the provided configuration.
    /// </summary>
    /// <param name="schedule">The schedule configuration to create.</param>
    /// <returns>The created schedule description.</returns>
    [HttpPost]
    public async Task<ActionResult<ScheduleDescription>> CreateSchedule([FromBody] CreateScheduleRequest createScheduleRequest)
    {
        if (createScheduleRequest == null)
        {
            return this.BadRequest("createScheduleRequest cannot be null");
        }

        try
        {
            ScheduleCreationOptions creationOptions = new ScheduleCreationOptions(createScheduleRequest.Id.ToString(), createScheduleRequest.OrchestrationName, createScheduleRequest.Interval)
            {
                OrchestrationInput = createScheduleRequest.Input,
                StartAt = createScheduleRequest.StartAt,
                EndAt = createScheduleRequest.EndAt,
                StartImmediatelyIfLate = true
            };

            ScheduleClient scheduleClient = await this.scheduledTaskClient.CreateScheduleAsync(creationOptions);
            ScheduleDescription description = await scheduleClient.DescribeAsync();

            this.logger.LogInformation("Created new schedule with ID: {ScheduleId}", createScheduleRequest.Id);

            return this.CreatedAtAction(nameof(GetSchedule), new { id = createScheduleRequest.Id }, description);
        }
        catch (ScheduleClientValidationException ex)
        {
            this.logger.LogError(ex, "Validation failed while creating schedule {ScheduleId}", createScheduleRequest.Id);
            return this.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error creating schedule {ScheduleId}", createScheduleRequest.Id);
            return this.StatusCode(500, "An error occurred while creating the schedule");
        }
    }

    /// <summary>
    /// Retrieves a specific schedule by its ID.
    /// </summary>
    /// <param name="id">The ID of the schedule to retrieve.</param>
    /// <returns>The schedule description if found.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ScheduleDescription>> GetSchedule(string id)
    {
        try
        {
            ScheduleDescription? schedule = await this.scheduledTaskClient.GetScheduleAsync(id);
            return this.Ok(schedule);
        }
        catch (ScheduleNotFoundException)
        {
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error retrieving schedule {ScheduleId}", id);
            return this.StatusCode(500, "An error occurred while retrieving the schedule");
        }
    }

    /// <summary>
    /// Lists all schedules, optionally filtered by status.
    /// </summary>
    /// <returns>A collection of schedule descriptions.</returns>
    [HttpGet("list")]
    public async Task<ActionResult<IEnumerable<ScheduleDescription>>> ListSchedules()
    {
        try
        {
            AsyncPageable<ScheduleDescription> schedules = this.scheduledTaskClient.ListSchedulesAsync();

            // add schedule result list 
            List<ScheduleDescription> scheduleList = new List<ScheduleDescription>();
            // Initialize the continuation token
            await foreach (ScheduleDescription schedule in schedules)
            {
                scheduleList.Add(schedule);
            }

            return this.Ok(scheduleList);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error retrieving schedules");
            return this.StatusCode(500, "An error occurred while retrieving schedules");
        }
    }

    /// <summary>
    /// Updates an existing schedule with new configuration.
    /// </summary>
    /// <param name="id">The ID of the schedule to update.</param>
    /// <param name="updateScheduleRequest">The new schedule configuration.</param>
    /// <returns>The updated schedule description.</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<ScheduleDescription>> UpdateSchedule(string id, [FromBody] UpdateScheduleRequest updateScheduleRequest)
    {
        if (updateScheduleRequest == null)
        {
            return this.BadRequest("Schedule cannot be null");
        }

        try
        {
            ScheduleClient scheduleClient = this.scheduledTaskClient.GetScheduleClient(id);

            ScheduleUpdateOptions updateOptions = new ScheduleUpdateOptions
            {
                OrchestrationName = updateScheduleRequest.OrchestrationName,
                OrchestrationInput = updateScheduleRequest.Input,
                StartAt = updateScheduleRequest.StartAt,
                EndAt = updateScheduleRequest.EndAt,
                Interval = updateScheduleRequest.Interval
            };

            await scheduleClient.UpdateAsync(updateOptions);
            return this.Ok(await scheduleClient.DescribeAsync());
        }
        catch (ScheduleNotFoundException)
        {
            return this.NotFound();
        }
        catch (ScheduleClientValidationException ex)
        {
            this.logger.LogError(ex, "Validation failed while updating schedule {ScheduleId}", id);
            return this.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error updating schedule {ScheduleId}", id);
            return this.StatusCode(500, "An error occurred while updating the schedule");
        }
    }

    /// <summary>
    /// Deletes a schedule by its ID.
    /// </summary>
    /// <param name="id">The ID of the schedule to delete.</param>
    /// <returns>No content if successful.</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSchedule(string id)
    {
        try
        {
            ScheduleClient scheduleClient = this.scheduledTaskClient.GetScheduleClient(id);
            await scheduleClient.DeleteAsync();
            return this.NoContent();
        }
        catch (ScheduleNotFoundException)
        {
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error deleting schedule {ScheduleId}", id);
            return this.StatusCode(500, "An error occurred while deleting the schedule");
        }
    }

    /// <summary>
    /// Pauses a running schedule.
    /// </summary>
    /// <param name="id">The ID of the schedule to pause.</param>
    /// <returns>The updated schedule description.</returns>
    [HttpPost("{id}/pause")]
    public async Task<ActionResult<ScheduleDescription>> PauseSchedule(string id)
    {
        try
        {
            ScheduleClient scheduleClient = this.scheduledTaskClient.GetScheduleClient(id);
            await scheduleClient.PauseAsync();
            return this.Ok(await scheduleClient.DescribeAsync());
        }
        catch (ScheduleNotFoundException)
        {
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error pausing schedule {ScheduleId}", id);
            return this.StatusCode(500, "An error occurred while pausing the schedule");
        }
    }

    /// <summary>
    /// Resumes a paused schedule.
    /// </summary>
    /// <param name="id">The ID of the schedule to resume.</param>
    /// <returns>The updated schedule description.</returns>
    [HttpPost("{id}/resume")]
    public async Task<ActionResult<ScheduleDescription>> ResumeSchedule(string id)
    {
        try
        {
            ScheduleClient scheduleClient = this.scheduledTaskClient.GetScheduleClient(id);
            await scheduleClient.ResumeAsync();
            return this.Ok(await scheduleClient.DescribeAsync());
        }
        catch (ScheduleNotFoundException)
        {
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error resuming schedule {ScheduleId}", id);
            return this.StatusCode(500, "An error occurred while resuming the schedule");
        }
    }
}