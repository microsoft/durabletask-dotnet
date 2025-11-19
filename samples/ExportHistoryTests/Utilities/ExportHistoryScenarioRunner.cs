using System.IO.Compression;
using System.Linq;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ExportHistoryTests.Infrastructure;
using ExportHistoryTests.Scenarios;
using ExportHistoryTests.Tasks;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;

namespace ExportHistoryTests.Utilities;

public sealed class ExportHistoryScenarioRunner
{
    readonly ExportHistoryTestFixture fixture;
    readonly TaskName orchestratorName = new(nameof(ExportHistoryTestOrchestrator));

    public ExportHistoryScenarioRunner(ExportHistoryTestFixture fixture)
    {
        this.fixture = fixture;
    }

    public async Task<ExportScenarioResult> ExecuteBatchScenarioAsync(
        ExportHistoryBatchScenario scenario,
        CancellationToken cancellation = default)
    {
        this.fixture.SkipIfNotConfigured();
        DurableTaskClient client = this.fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient not initialized.");
        ExportHistoryClient exportClient = this.fixture.ExportHistoryClient ?? throw new InvalidOperationException("ExportHistoryClient not initialized.");

        string jobId = this.BuildJobId("batch", scenario);
        string containerName = this.ResolveContainerName(scenario);
        string plannedPrefix = this.ResolvePlannedPrefix(ExportMode.Batch, jobId, scenario.CustomPrefix, scenario.UseDefaultPrefix);

        if (!scenario.PreserveExportedBlobs)
        {
            await this.DeleteBlobsAsync(containerName, plannedPrefix, cancellation);
        }

        List<ExportInstanceRecord> instanceRecords = await this.CreateBatchInstancesAsync(scenario, jobId, cancellation);
        (DateTimeOffset completedFrom, DateTimeOffset completedTo) = ComputeTimeWindow(instanceRecords);

        ExportDestination? destination = this.CreateDestination(containerName, scenario.UseDefaultPrefix, plannedPrefix, scenario.UseCustomContainer || !scenario.UseDefaultPrefix || !string.IsNullOrEmpty(scenario.CustomPrefix));

        List<OrchestrationRuntimeStatus>? runtimeStatuses = scenario.RuntimeStatuses?.ToList();

        ExportJobCreationOptions options = new(
            ExportMode.Batch,
            completedFrom,
            completedTo,
            destination,
            jobId,
            new ExportFormat(scenario.Format),
            runtimeStatuses,
            scenario.MaxInstancesPerBatch ?? (scenario.ForceMultipleBatches ? 2 : 100));

        ExportHistoryJobClient jobClient = exportClient.GetJobClient(jobId);
        await jobClient.CreateAsync(options, cancellation);

        ExportJobDescription description = await this.WaitForBatchCompletionAsync(jobClient, cancellation);

        string actualPrefix = description.Config?.Destination.Prefix ?? plannedPrefix;
        string actualContainer = description.Config?.Destination.Container ?? containerName;

        IReadOnlyList<BlobItem> blobs = await this.ListBlobsAsync(actualContainer, actualPrefix, cancellation);
        IReadOnlyList<ExportedBlobArtifact> artifacts = await this.DownloadArtifactsAsync(actualContainer, blobs, scenario.Format, cancellation);

        bool requiresManualCleanup = scenario.KeepJobAlive;
        if (!requiresManualCleanup)
        {
            await jobClient.DeleteAsync(cancellation);
            await this.PurgeInstancesAsync(instanceRecords.Select(i => i.InstanceId), cancellation);
            if (!scenario.PreserveExportedBlobs)
            {
                await this.DeleteBlobsAsync(actualContainer, actualPrefix, cancellation);
            }
        }

        return new ExportScenarioResult(
            jobId,
            ExportMode.Batch,
            description,
            instanceRecords,
            artifacts,
            actualContainer,
            actualPrefix,
            scenario,
            null,
            requiresManualCleanup,
            jobClient,
            instanceRecords.Select(i => i.InstanceId).ToList());
    }

    public async Task<ExportScenarioResult> ExecuteContinuousScenarioAsync(
        ExportHistoryContinuousScenario scenario,
        CancellationToken cancellation = default)
    {
        this.fixture.SkipIfNotConfigured();
        DurableTaskClient client = this.fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient not initialized.");
        ExportHistoryClient exportClient = this.fixture.ExportHistoryClient ?? throw new InvalidOperationException("ExportHistoryClient not initialized.");

        string jobId = this.BuildJobId("continuous", scenario);
        string containerName = this.ResolveContainerName(scenario.UseCustomContainer, scenario.CustomContainerSuffix);
        string plannedPrefix = this.ResolvePlannedPrefix(ExportMode.Continuous, jobId, scenario.CustomPrefix, useDefaultPrefix: false);

        await this.DeleteBlobsAsync(containerName, plannedPrefix, cancellation);

        List<ExportInstanceRecord> initialInstances = await this.CreateInstancesAsync(
            scenario.InitialInstances,
            jobId,
            scenario,
            ExportInstanceOutcome.Completed,
            cancellation);

        (DateTimeOffset completedFrom, _) = ComputeTimeWindow(initialInstances);

        ExportDestination destination = this.CreateDestination(containerName, useDefaultPrefix: false, plannedPrefix, true)!;

        ExportJobCreationOptions options = new(
            ExportMode.Continuous,
            completedFrom,
            completedTimeTo: null,
            destination,
            jobId,
            new ExportFormat(scenario.Format));

        ExportHistoryJobClient jobClient = exportClient.GetJobClient(jobId);
        await jobClient.CreateAsync(options, cancellation);

        ExportJobDescription description = await this.WaitForContinuousProgressAsync(jobClient, scenario.ExpectedInitialExports, cancellation);

        // Start additional instances after job is active.
        List<ExportInstanceRecord> additionalInstances = await this.CreateInstancesAsync(
            scenario.AdditionalInstances,
            jobId,
            scenario,
            ExportInstanceOutcome.Completed,
            cancellation);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + scenario.EffectiveObservationWindow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            description = await jobClient.DescribeAsync(cancellation);
            if (description.ExportedInstances >= scenario.ExpectedTotalExports)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
        }

        string actualPrefix = description.Config?.Destination.Prefix ?? plannedPrefix;
        string actualContainer = description.Config?.Destination.Container ?? containerName;
        IReadOnlyList<BlobItem> blobs = await this.ListBlobsAsync(actualContainer, actualPrefix, cancellation);
        IReadOnlyList<ExportedBlobArtifact> artifacts = await this.DownloadArtifactsAsync(actualContainer, blobs, scenario.Format, cancellation);

        bool requiresManualCleanup = scenario.KeepJobAlive;
        if (!requiresManualCleanup)
        {
            await jobClient.DeleteAsync(cancellation);
            await this.PurgeInstancesAsync(initialInstances.Concat(additionalInstances).Select(i => i.InstanceId), cancellation);
            await this.DeleteBlobsAsync(actualContainer, actualPrefix, cancellation);
        }

        List<ExportInstanceRecord> allInstances = new(initialInstances.Count + additionalInstances.Count);
        allInstances.AddRange(initialInstances);
        allInstances.AddRange(additionalInstances);

        return new ExportScenarioResult(
            jobId,
            ExportMode.Continuous,
            description,
            allInstances,
            artifacts,
            actualContainer,
            actualPrefix,
            null,
            scenario,
            requiresManualCleanup,
            jobClient,
            allInstances.Select(i => i.InstanceId).ToList());
    }

    public async Task CleanupAsync(ExportScenarioResult result, CancellationToken cancellation = default)
    {
        if (!result.RequiresManualCleanup)
        {
            return;
        }

        try
        {
            await result.JobClient.DeleteAsync(cancellation);
        }
        catch (ExportJobNotFoundException)
        {
            // Already deleted by the test.
        }
        await this.PurgeInstancesAsync(result.InstanceIdsToPurge, cancellation);
        await this.DeleteBlobsAsync(result.ContainerName, result.Prefix, cancellation);
    }

    async Task<List<ExportInstanceRecord>> CreateBatchInstancesAsync(
        ExportHistoryBatchScenario scenario,
        string jobId,
        CancellationToken cancellation)
    {
        List<ExportInstanceRecord> instances = new();
        if (scenario.CompletedInstances > 0)
        {
            instances.AddRange(await this.CreateInstancesAsync(
                scenario.CompletedInstances,
                jobId,
                scenario,
                ExportInstanceOutcome.Completed,
                cancellation));
        }

        if (scenario.FailedInstances > 0)
        {
            instances.AddRange(await this.CreateInstancesAsync(
                scenario.FailedInstances,
                jobId,
                scenario,
                ExportInstanceOutcome.Failed,
                cancellation));
        }

        if (scenario.TerminatedInstances > 0)
        {
            instances.AddRange(await this.CreateInstancesAsync(
                scenario.TerminatedInstances,
                jobId,
                scenario,
                ExportInstanceOutcome.LongRunning,
                cancellation));
        }

        return instances;
    }

    async Task<List<ExportInstanceRecord>> CreateInstancesAsync(
        int count,
        string jobId,
        ExportHistoryBatchScenario scenario,
        ExportInstanceOutcome outcome,
        CancellationToken cancellation)
    {
        DurableTaskClient client = this.fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient not initialized.");
        List<ExportInstanceRecord> records = new(count);

        for (int i = 0; i < count; i++)
        {
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 16);
            string instanceId = $"{jobId}-{outcome.ToString()[0]}{i}-{shortGuid}";
            if (instanceId.Length > 100)
            {
                // Truncate the jobId portion to fit within 100 char limit
                int maxJobIdLength = 100 - 1 - 2 - 1 - 16; // Leave room for "-{outcome[0]}{i}-{guid16}"
                instanceId = $"{jobId.Substring(0, Math.Min(maxJobIdLength, jobId.Length))}-{outcome.ToString()[0]}{i}-{shortGuid}";
            }

            ExportGenerationRequest request = new(
                jobId,
                outcome,
                ActivityCount: scenario.ActivityCount,
                UseFanOut: scenario.UseFanOut,
                UseSubOrchestrator: scenario.UseSubOrchestrator,
                UseLargePayload: scenario.UseLargePayload,
                EmitCustomStatus: scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyStatistics));

            StartOrchestrationOptions options = new(instanceId);
            await client.ScheduleNewOrchestrationInstanceAsync(this.orchestratorName, request, options, cancellation);

            OrchestrationMetadata metadata = outcome switch
            {
                ExportInstanceOutcome.LongRunning => await this.TerminateLongRunningInstanceAsync(client, instanceId, cancellation),
                _ => await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: false, cancellation),
            };

            records.Add(new ExportInstanceRecord(instanceId, metadata.RuntimeStatus, metadata.LastUpdatedAt));
        }

        return records;
    }

    async Task<List<ExportInstanceRecord>> CreateInstancesAsync(
        int count,
        string jobId,
        ExportHistoryContinuousScenario scenario,
        ExportInstanceOutcome outcome,
        CancellationToken cancellation)
    {
        DurableTaskClient client = this.fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient not initialized.");
        List<ExportInstanceRecord> records = new(count);

        for (int i = 0; i < count; i++)
        {
            string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 16);
            string instanceId = $"{jobId}-{outcome.ToString()[0]}{i}-{shortGuid}";
            if (instanceId.Length > 100)
            {
                // Truncate the jobId portion to fit within 100 char limit
                int maxJobIdLength = 100 - 1 - 2 - 1 - 16; // Leave room for "-{outcome[0]}{i}-{guid16}"
                instanceId = $"{jobId.Substring(0, Math.Min(maxJobIdLength, jobId.Length))}-{outcome.ToString()[0]}{i}-{shortGuid}";
            }

            ExportGenerationRequest request = new(
                jobId,
                outcome,
                ActivityCount: scenario.ActivityCount,
                UseFanOut: scenario.UseFanOut,
                UseSubOrchestrator: scenario.UseSubOrchestrator,
                UseLargePayload: scenario.UseLargePayload);

            StartOrchestrationOptions options = new(instanceId);
            await client.ScheduleNewOrchestrationInstanceAsync(this.orchestratorName, request, options, cancellation);

            OrchestrationMetadata metadata = outcome switch
            {
                ExportInstanceOutcome.LongRunning => await this.TerminateLongRunningInstanceAsync(client, instanceId, cancellation),
                _ => await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: false, cancellation),
            };

            records.Add(new ExportInstanceRecord(instanceId, metadata.RuntimeStatus, metadata.LastUpdatedAt));
        }

        return records;
    }

    static (DateTimeOffset From, DateTimeOffset To) ComputeTimeWindow(IReadOnlyList<ExportInstanceRecord> records)
    {
        DateTimeOffset min = records.Min(r => r.CompletedTimeUtc);
        DateTimeOffset max = records.Max(r => r.CompletedTimeUtc);

        DateTimeOffset from = min.AddSeconds(-5);
        DateTimeOffset to = max.AddSeconds(5);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (to > now)
        {
            to = now;
        }

        return (from, to);
    }

    async Task<OrchestrationMetadata> TerminateLongRunningInstanceAsync(
        DurableTaskClient client,
        string instanceId,
        CancellationToken cancellation)
    {
        await client.WaitForInstanceStartAsync(instanceId, getInputsAndOutputs: false, cancellation);
        await client.TerminateInstanceAsync(instanceId, new TerminateInstanceOptions { Output = "terminated" }, cancellation);
        return await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: false, cancellation);
    }

    async Task<ExportJobDescription> WaitForBatchCompletionAsync(
        ExportHistoryJobClient jobClient,
        CancellationToken cancellation)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        ExportJobDescription description = await jobClient.DescribeAsync(cancellation);

        while (DateTime.UtcNow < deadline)
        {
            if (description.Status is ExportJobStatus.Completed or ExportJobStatus.Failed)
            {
                return description;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            description = await jobClient.DescribeAsync(cancellation);
        }

        throw new TimeoutException("Export job did not complete within the expected time window.");
    }

    async Task<ExportJobDescription> WaitForContinuousProgressAsync(
        ExportHistoryJobClient jobClient,
        int expectedExports,
        CancellationToken cancellation)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(5);
        ExportJobDescription description = await jobClient.DescribeAsync(cancellation);

        while (DateTime.UtcNow < deadline)
        {
            if (description.Status == ExportJobStatus.Active && description.ExportedInstances >= expectedExports)
            {
                return description;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            description = await jobClient.DescribeAsync(cancellation);
        }

        throw new TimeoutException("Continuous export job did not reach the expected progress.");
    }

    string BuildJobId(string mode, ExportHistoryBatchScenario scenario)
    {
        string prefix = string.IsNullOrWhiteSpace(scenario.JobIdPrefix) ? $"export-{mode}" : scenario.JobIdPrefix;
        return $"{prefix}-{scenario.Name}-{Guid.NewGuid():N}";
    }

    string BuildJobId(string mode, ExportHistoryContinuousScenario scenario)
    {
        return $"export-{mode}-{scenario.Name}-{Guid.NewGuid():N}";
    }

    string ResolveContainerName(ExportHistoryBatchScenario scenario)
    {
        return this.ResolveContainerName(scenario.UseCustomContainer, scenario.CustomContainerSuffix ?? scenario.Name);
    }

    string ResolveContainerName(bool useCustomContainer, string? suffix)
    {
        if (!useCustomContainer)
        {
            return this.fixture.Environment.ContainerName;
        }

        string sanitizedSuffix = string.IsNullOrWhiteSpace(suffix) ? "custom" : suffix.Replace(".", "-").ToLowerInvariant();
        return $"{this.fixture.Environment.ContainerName}-{sanitizedSuffix}";
    }

    string ResolvePlannedPrefix(
        ExportMode mode,
        string jobId,
        string? customPrefix,
        bool useDefaultPrefix)
    {
        if (!string.IsNullOrWhiteSpace(customPrefix))
        {
            return NormalizePrefix(customPrefix);
        }

        if (useDefaultPrefix && !string.IsNullOrWhiteSpace(this.fixture.Environment.DefaultPrefix))
        {
            return NormalizePrefix(this.fixture.Environment.DefaultPrefix);
        }

        return $"{mode.ToString().ToLowerInvariant()}-{jobId}/";
    }

    ExportDestination? CreateDestination(
        string containerName,
        bool useDefaultPrefix,
        string prefix,
        bool needsDestination)
    {
        if (!needsDestination && useDefaultPrefix)
        {
            return null;
        }

        ExportDestination destination = new(containerName);
        destination.Prefix = useDefaultPrefix ? null : NormalizePrefix(prefix);
        return destination;
    }

    async Task<List<BlobItem>> ListBlobsAsync(string containerName, string prefix, CancellationToken cancellation)
    {
        BlobContainerClient container = await this.fixture.GetContainerClientAsync(containerName, cancellation);
        List<BlobItem> blobs = new();
        await foreach (BlobItem blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellation))
        {
            blobs.Add(blob);
        }

        return blobs;
    }

    async Task DeleteBlobsAsync(string containerName, string prefix, CancellationToken cancellation)
    {
        BlobContainerClient container = await this.fixture.GetContainerClientAsync(containerName, cancellation);
        await foreach (BlobItem blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellation))
        {
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellation);
        }
    }

    async Task<List<ExportedBlobArtifact>> DownloadArtifactsAsync(
        string containerName,
        IReadOnlyList<BlobItem> blobs,
        ExportFormatKind format,
        CancellationToken cancellation)
    {
        BlobContainerClient container = await this.fixture.GetContainerClientAsync(containerName, cancellation);
        List<ExportedBlobArtifact> artifacts = new(blobs.Count);

        foreach (BlobItem blob in blobs)
        {
            BlobClient blobClient = container.GetBlobClient(blob.Name);
            BlobDownloadResult download = await blobClient.DownloadContentAsync(cancellation);
            byte[] raw = download.Content.ToArray();
            bool isCompressed = string.Equals(download.Details.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase);
            string text = isCompressed ? DecompressGzip(raw) : Encoding.UTF8.GetString(raw);

            artifacts.Add(new ExportedBlobArtifact(
                containerName,
                blob.Name,
                blob.Properties.ContentType ?? download.Details.ContentType ?? string.Empty,
                blob.Properties.ContentLength ?? raw.Length,
                download.Details.Metadata as IReadOnlyDictionary<string, string> ?? new Dictionary<string, string>(download.Details.Metadata),
                text,
                isCompressed,
                format));
        }

        return artifacts;
    }

    async Task PurgeInstancesAsync(IEnumerable<string> instanceIds, CancellationToken cancellation)
    {
        DurableTaskClient client = this.fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient not initialized.");

        foreach (string instanceId in instanceIds)
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                continue;
            }

            try
            {
                await client.PurgeInstanceAsync(instanceId, cancellation);
            }
            catch
            {
                // Swallow purge errors to avoid flakiness.
            }
        }
    }

    static string NormalizePrefix(string prefix)
    {
        string normalized = prefix.Replace("\\", "/");
        if (!normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized += "/";
        }

        return normalized;
    }

    static string DecompressGzip(byte[] content)
    {
        using MemoryStream source = new(content);
        using GZipStream gzip = new(source, CompressionMode.Decompress);
        using StreamReader reader = new(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

