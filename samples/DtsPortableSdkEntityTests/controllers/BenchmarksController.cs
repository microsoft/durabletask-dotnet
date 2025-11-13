// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Security.Cryptography;
using DurableTask.Core.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;

namespace DtsPortableSdkEntityTests;


[Route("benchmarks")]
[ApiController]
public class BenchmarksController(
    DurableTaskClient durableTaskClient,
    ILogger<BenchmarksController> logger) : ControllerBase
{
    readonly DurableTaskClient durableTaskClient = durableTaskClient;
    readonly ILogger<BenchmarksController> logger = logger;

    // we are planning to create some benchmarks here at some point but for now these are just very basic entity tests
    // that allow us to read/update/delete a counter entity via a simple REST-like api

    // POST http://localhost:5008/benchmarks/counter/xyz/increment
    [HttpPost("counter/{key}/increment")]
    public async Task<ActionResult> CounterIncrement([FromRoute] string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { error = "The 'key' route parameter must not be empty." });
        }

        EntityInstanceId entityId = new(nameof(Counter), key);

        logger.LogInformation("Sending signal 'Increment' to {entityId}.", entityId);

        Stopwatch sw = Stopwatch.StartNew();

        await durableTaskClient.Entities.SignalEntityAsync(entityId, nameof(Counter.Increment));

        sw.Stop();

        logger.LogInformation(
            "Sent signal 'Increment' to {entityId} in {time}ms!",
            entityId,
            sw.Elapsed.TotalMilliseconds);

        return Ok(new
        {
            message = $"Sent signal 'Increment' to {entityId} in {sw.Elapsed.TotalMilliseconds:F3}ms."
        });
    }

    // GET http://localhost:5008/benchmarks/counter/xyz
    [HttpGet("counter/{key}")]
    public async Task<ActionResult> CounterGet([FromRoute] string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { error = "The 'key' route parameter must not be empty." });
        }

        EntityInstanceId entityId = new(nameof(Counter), key);

        logger.LogInformation("Reading state of {entityId}.", entityId);

        EntityMetadata<int>? entityMetadata =
            await durableTaskClient.Entities.GetEntityAsync<int>(entityId);

        if (entityMetadata == null)
        {
            return NotFound(new
            {
                message = $"Entity {entityId} does not exist."
            });
        }
        else
        {
            return Ok(new
            {
                message = $"Entity {entityId} has state {entityMetadata.State}."
            });
        }
    }

    // DELETE http://localhost:5008/benchmarks/counter/xyz
    [HttpDelete("counter/{key}")]
    public async Task<ActionResult> CounterDelete([FromRoute] string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { error = "The 'key' route parameter must not be empty." });
        }

        EntityInstanceId entityId = new(nameof(Counter), key);

        logger.LogInformation("Deleting state of {entityId}.", entityId);

        await durableTaskClient.Entities.SignalEntityAsync(entityId, "delete");

        return Ok(new
        {
            message = $"Sent deletion signal to {entityId}."
        });
    }
}