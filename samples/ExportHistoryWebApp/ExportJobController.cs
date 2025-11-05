// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;
using ExportHistoryWebApp.Models;

namespace ExportHistoryWebApp.Controllers;

/// <summary>
/// Controller for managing export history jobs through a REST API.
/// Provides endpoints for creating, reading, listing, and deleting export jobs.
/// </summary>
[ApiController]
[Route("export-jobs")]
public class ExportJobController : ControllerBase
{
    readonly ExportHistoryClient exportHistoryClient;
    readonly ILogger<ExportJobController> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportJobController"/> class.
    /// </summary>
    /// <param name="exportHistoryClient">Client for managing export history jobs.</param>
    /// <param name="logger">Logger for recording controller operations.</param>
    public ExportJobController(
        ExportHistoryClient exportHistoryClient,
        ILogger<ExportJobController> logger)
    {
        this.exportHistoryClient = exportHistoryClient ?? throw new ArgumentNullException(nameof(exportHistoryClient));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new export job based on the provided configuration.
    /// </summary>
    /// <param name="request">The export job creation request.</param>
    /// <returns>The created export job description.</returns>
    [HttpPost]
    public async Task<ActionResult<ExportJobDescription>> CreateExportJob([FromBody] CreateExportJobRequest request)
    {
        if (request == null)
        {
            return this.BadRequest("createExportJobRequest cannot be null");
        }

        try
        {
            ExportDestination? destination = null;
            if (!string.IsNullOrEmpty(request.Container))
            {
                destination = new ExportDestination(request.Container)
                {
                    Prefix = request.Prefix,
                };
            }

            ExportJobCreationOptions creationOptions = new ExportJobCreationOptions(
                mode: request.Mode,
                completedTimeFrom: request.CompletedTimeFrom,
                completedTimeTo: request.CompletedTimeTo,
                destination: destination,
                jobId: request.JobId,
                format: request.Format,
                runtimeStatus: request.RuntimeStatus,
                maxInstancesPerBatch: request.MaxInstancesPerBatch);

            ExportHistoryJobClient jobClient = await this.exportHistoryClient.CreateJobAsync(creationOptions);
            ExportJobDescription description = await jobClient.DescribeAsync();

            this.logger.LogInformation("Created new export job with ID: {JobId}", description.JobId);

            return this.CreatedAtAction(nameof(GetExportJob), new { id = description.JobId }, description);
        }
        catch (ArgumentException ex)
        {
            this.logger.LogError(ex, "Validation failed while creating export job {JobId}", request.JobId);
            return this.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error creating export job {JobId}", request.JobId);
            return this.StatusCode(500, "An error occurred while creating the export job");
        }
    }

    /// <summary>
    /// Retrieves a specific export job by its ID.
    /// </summary>
    /// <param name="id">The ID of the export job to retrieve.</param>
    /// <returns>The export job description if found.</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ExportJobDescription>> GetExportJob(string id)
    {
        try
        {
            ExportJobDescription? job = await this.exportHistoryClient.GetJobAsync(id);
            return this.Ok(job);
        }
        catch (ExportJobNotFoundException)
        {
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error retrieving export job {JobId}", id);
            return this.StatusCode(500, "An error occurred while retrieving the export job");
        }
    }

    /// <summary>
    /// Lists all export jobs, optionally filtered by query parameters.
    /// </summary>
    /// <param name="status">Optional filter by job status.</param>
    /// <param name="jobIdPrefix">Optional filter by job ID prefix.</param>
    /// <param name="createdFrom">Optional filter for jobs created after this time.</param>
    /// <param name="createdTo">Optional filter for jobs created before this time.</param>
    /// <param name="pageSize">Optional page size for pagination.</param>
    /// <param name="continuationToken">Optional continuation token for pagination.</param>
    /// <returns>A collection of export job descriptions.</returns>
    [HttpGet("list")]
    public async Task<ActionResult<IEnumerable<ExportJobDescription>>> ListExportJobs(
        [FromQuery] ExportJobStatus? status = null,
        [FromQuery] string? jobIdPrefix = null,
        [FromQuery] DateTimeOffset? createdFrom = null,
        [FromQuery] DateTimeOffset? createdTo = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? continuationToken = null)
    {
        this.logger.LogInformation("GET list endpoint called with method: {Method}", this.HttpContext.Request.Method);
        try
        {
            ExportJobQuery? query = null;
            if (
                status.HasValue ||
                !string.IsNullOrEmpty(jobIdPrefix) ||
                createdFrom.HasValue ||
                createdTo.HasValue ||
                pageSize.HasValue ||
                !string.IsNullOrEmpty(continuationToken)
            )
            {
                query = new ExportJobQuery
                {
                    Status = status,
                    JobIdPrefix = jobIdPrefix,
                    CreatedFrom = createdFrom,
                    CreatedTo = createdTo,
                    PageSize = pageSize,
                    ContinuationToken = continuationToken,
                };
            }

            AsyncPageable<ExportJobDescription> jobs = this.exportHistoryClient.ListJobsAsync(query);

            // Collect all jobs from the async pageable
            List<ExportJobDescription> jobList = new List<ExportJobDescription>();
            await foreach (ExportJobDescription job in jobs)
            {
                jobList.Add(job);
            }

            return this.Ok(jobList);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error retrieving export jobs");
            return this.StatusCode(500, "An error occurred while retrieving export jobs");
        }
    }

    /// <summary>
    /// Deletes an export job by its ID.
    /// </summary>
    /// <param name="id">The ID of the export job to delete.</param>
    /// <returns>No content if successful.</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteExportJob(string id)
    {
        this.logger.LogInformation("DELETE endpoint called for job ID: {JobId}", id);
        try
        {
            ExportHistoryJobClient jobClient = this.exportHistoryClient.GetJobClient(id);
            await jobClient.DeleteAsync();
            this.logger.LogInformation("Successfully deleted export job {JobId}", id);
            return this.NoContent();
        }
        catch (ExportJobNotFoundException)
        {
            this.logger.LogWarning("Export job {JobId} not found for deletion", id);
            return this.NotFound();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error deleting export job {JobId}", id);
            return this.StatusCode(500, "An error occurred while deleting the export job");
        }
    }
}

