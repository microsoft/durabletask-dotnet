using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace AspNetWebApp;

[Route("scenarios")]
[ApiController]
public partial class ScenariosController(
    DurableTaskClient durableTaskClient,
    ILogger<ScenariosController> logger) : ControllerBase
{
    readonly DurableTaskClient durableTaskClient = durableTaskClient;
    readonly ILogger<ScenariosController> logger = logger;

    [HttpPost("hellocities")]
    public async Task<ActionResult> RunHelloCities([FromQuery] int? count, [FromQuery] string? prefix)
    {
        if (count is null || count < 1)
        {
            return this.BadRequest(new { error = "A 'count' query string parameter is required and it must contain a positive number." });
        }

        // Generate a semi-unique prefix for the instance IDs to simplify tracking
        prefix ??= $"hellocities-{count}-";
        prefix += DateTime.UtcNow.ToString("yyyyMMdd-hhmmss");

        this.logger.LogInformation("Scheduling {count} orchestrations with a prefix of '{prefix}'...", count, prefix);

        Stopwatch sw = Stopwatch.StartNew();
        await Enumerable.Range(0, count.Value).ParallelForEachAsync(1000, i =>
        {
            string instanceId = $"{prefix}-{i:X16}";
            return this.durableTaskClient.ScheduleNewHelloCitiesInstanceAsync(
                input: null!,
                new StartOrchestrationOptions(instanceId));
        });

        sw.Stop();
        this.logger.LogInformation(
            "All {count} orchestrations were scheduled successfully in {time}ms!",
            count,
            sw.ElapsedMilliseconds);
        return this.Ok(new
        {
            message = $"Scheduled {count} orchestrations prefixed with '{prefix}' in {sw.ElapsedMilliseconds}."
        });
    }
}
