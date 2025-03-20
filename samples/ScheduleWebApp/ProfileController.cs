// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ScheduledTasks;
using ScheduleWebApp.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ScheduleWebApp.Controllers;

/// <summary>
/// Controller for managing orchestration profiles and instances through a REST API.
/// Provides endpoints for creating orchestration instances and schedules.
/// </summary>
[ApiController]
[Route("instances")]
public class ProfileController : ControllerBase
{
    readonly ScheduledTaskClient scheduledTaskClient;
    readonly DurableTaskClient durableTaskClient;
    readonly ILogger<ProfileController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileController"/> class.
    /// </summary>
    /// <param name="scheduledTaskClient">Client for managing scheduled tasks.</param>
    /// <param name="durableTaskClient">Client for starting orchestrations.</param>
    /// <param name="logger">Logger for recording controller operations.</param>
    public ProfileController(
        ScheduledTaskClient scheduledTaskClient,
        DurableTaskClient durableTaskClient,
        ILogger<ProfileController> logger)
    {
        this.scheduledTaskClient = scheduledTaskClient ?? throw new ArgumentNullException(nameof(scheduledTaskClient));
        this.durableTaskClient = durableTaskClient ?? throw new ArgumentNullException(nameof(durableTaskClient));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    /// <summary>
    /// Creates multiple instances of an orchestration.
    /// </summary>
    /// <param name="orchestrationName">The name of the orchestration to create instances of.</param>
    /// <param name="count">The number of instances to create.</param>
    /// <param name="input">Optional input for the orchestration.</param>
    /// <returns>A list of created instance IDs.</returns>
    [HttpPost]
    public async Task<ActionResult<List<string>>> CreateInstances(
        [FromQuery] int count,
        [FromBody] object input = null)
    {
        if (count <= 0)
        {
            return this.BadRequest("count must be greater than 0");
        }

        try
        {
            List<string> instanceIds = new List<string>();
            const int parallelTasks = 12;
            
            // Calculate items per task
            int itemsPerTask = count / parallelTasks;
            int remainder = count % parallelTasks;
            
            // Create tasks for parallel execution
            var tasks = new Task<List<string>>[parallelTasks];
            
            for (int taskIndex = 0; taskIndex < parallelTasks; taskIndex++)
            {
                // Calculate start and count for this task
                int startIndex = taskIndex * itemsPerTask;
                int taskItemCount = itemsPerTask + (taskIndex == parallelTasks - 1 ? remainder : 0);
                
                tasks[taskIndex] = Task.Run(async () =>
                {
                    var taskInstanceIds = new List<string>();
                    for (int i = 0; i < taskItemCount; i++)
                    {
                        string instanceId = await this.durableTaskClient.ScheduleNewOrchestrationInstanceAsync(
                            "HelloWorldOrchestrator", 
                            input);
                        
                        taskInstanceIds.Add(instanceId);
                    }
                    return taskInstanceIds;
                });
            }
            
            // Wait for all tasks to complete and collect results
            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                instanceIds.AddRange(result);
            }
            
            // Wait for all orchestration instances to complete
            await Task.WhenAll(instanceIds.Select(async instanceId =>
            {
                await this.durableTaskClient.WaitForInstanceCompletionAsync(instanceId);
            }));

            // Log completion
            this.logger.LogInformation("All {Count} orchestration instances completed", instanceIds.Count);

            return this.Ok(instanceIds);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error creating orchestration instances");
            return this.StatusCode(500, "An error occurred while creating orchestration instances");
        }
    }

    // add another endpoint to call purge instances
    [HttpPost("purge")]
    public async Task<ActionResult<List<string>>> PurgeInstances()
    {   
        try
        {
            // get all instances of the orchestration
            PurgeResult purgeResult = await this.durableTaskClient.PurgeInstancesAsync(
                null,
                null);
            
            // log the results
            this.logger.LogInformation("Purged {Count} orchestration instances", purgeResult.PurgedInstanceCount);

            return this.Ok(purgeResult);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error purging orchestration instances");
            return this.StatusCode(500, "An error occurred while purging orchestration instances");
        }
    }
}