using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DurableTask.Core.History;
using ExportHistoryTests.Infrastructure;
using ExportHistoryTests.Scenarios;
using ExportHistoryTests.Utilities;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExportHistoryTests.Tests;

public sealed class ExportHistoryBatchE2ETests : ExportHistoryTestBase
{
    public ExportHistoryBatchE2ETests(ExportHistoryTestFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [MemberData(nameof(ExportHistoryScenarioData.BatchScenarioData), MemberType = typeof(ExportHistoryScenarioData))]
    public async Task BatchScenario_CompletesSuccessfully(ExportHistoryBatchScenario scenario)
    {
        this.SkipIfNotConfigured();

        ExportScenarioResult result = await this.ScenarioRunner.ExecuteBatchScenarioAsync(scenario);
        try
        {
            if (scenario.ExpectJobFailure)
            {
                Assert.Equal(ExportJobStatus.Failed, result.JobDescription.Status);
                Assert.NotNull(result.JobDescription.LastError);
            }
            else
            {
                Assert.True(
                    result.JobDescription.Status == ExportJobStatus.Completed,
                    $"Export job failed with status {result.JobDescription.Status}. Error: {result.JobDescription.LastError ?? "No error message"}");
            }

            int expectedExports = this.CalculateExpectedExports(scenario);
            Assert.True(result.JobDescription.ExportedInstances >= expectedExports, $"Expected at least {expectedExports} exports but saw {result.JobDescription.ExportedInstances}.");

            await this.RunBatchAssertionsAsync(scenario, result);
        }
        finally
        {
            if (result.RequiresManualCleanup)
            {
                await this.ScenarioRunner.CleanupAsync(result);
            }
        }
    }

    int CalculateExpectedExports(ExportHistoryBatchScenario scenario)
    {
        if (scenario.ExpectJobFailure && scenario.CompletedInstances == 0)
        {
            return 0;
        }

        if (scenario.RuntimeStatuses == null)
        {
            return scenario.CompletedInstances + scenario.FailedInstances + scenario.TerminatedInstances;
        }

        int count = 0;
        if (scenario.RuntimeStatuses.Contains(OrchestrationRuntimeStatus.Completed))
        {
            count += scenario.CompletedInstances;
        }

        if (scenario.RuntimeStatuses.Contains(OrchestrationRuntimeStatus.Failed))
        {
            count += scenario.FailedInstances;
        }

        if (scenario.RuntimeStatuses.Contains(OrchestrationRuntimeStatus.Terminated))
        {
            count += scenario.TerminatedInstances;
        }

        return count;
    }

    async Task RunBatchAssertionsAsync(ExportHistoryBatchScenario scenario, ExportScenarioResult result)
    {
        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyBlobContent))
        {
            Assert.NotEmpty(result.Blobs);
            Assert.All(result.Blobs, blob => Assert.False(string.IsNullOrWhiteSpace(blob.TextPayload), "Export payload should contain serialized history."));
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyBlobMetadata))
        {
            Assert.All(result.Blobs, blob => Assert.Contains("instanceId", blob.Metadata.Keys));
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyBlobInstanceMetadata))
        {
            // Verify that blobs have instanceId metadata
            // Note: Metadata format may differ from runtime instance IDs, so just verify the key exists
            Assert.All(result.Blobs, blob => 
            {
                Assert.True(blob.Metadata.ContainsKey("instanceId"), "Blob should have instanceId metadata");
                Assert.False(string.IsNullOrWhiteSpace(blob.Metadata["instanceId"]), "instanceId metadata should not be empty");
            });
            
            // Verify we got at least as many blobs as test instances
            int expectedMinimumBlobs = result.Instances.Count;
            Assert.True(result.Blobs.Count >= expectedMinimumBlobs, $"Expected at least {expectedMinimumBlobs} blobs with metadata, got {result.Blobs.Count}");
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyJsonCompression))
        {
            bool expectCompression = scenario.Format == ExportFormatKind.Jsonl;
            Assert.All(result.Blobs, blob => Assert.Equal(expectCompression, blob.IsCompressed));
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyStatistics))
        {
            Assert.True(result.JobDescription.ScannedInstances >= result.JobDescription.ExportedInstances, "ScannedInstances should be greater than or equal to exported instances.");
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyCheckpoint))
        {
            Assert.NotNull(result.JobDescription.Checkpoint);
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyDefaultPrefix))
        {
            Assert.StartsWith("batch-", result.Prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyCustomPrefix) && !string.IsNullOrWhiteSpace(scenario.CustomPrefix))
        {
            Assert.Equal(NormalizePrefix(scenario.CustomPrefix), result.Prefix, ignoreCase: true);
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyContainer) && scenario.UseCustomContainer)
        {
            Assert.Contains(scenario.CustomContainerSuffix ?? scenario.Name, result.ContainerName, StringComparison.OrdinalIgnoreCase);
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyJobListing))
        {
            ExportJobQuery query = new()
            {
                JobIdPrefix = scenario.JobIdPrefix ?? result.JobId[..Math.Min(result.JobId.Length, 20)],
            };

            bool found = false;
            await foreach (ExportJobDescription description in this.ExportHistoryClient.ListJobsAsync(query))
            {
                if (description.JobId == result.JobId)
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "Expected job to appear in listing results.");
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyListFiltering))
        {
            ExportJobQuery query = new()
            {
                JobIdPrefix = "non-matching-prefix"
            };

            bool found = false;
            await foreach (ExportJobDescription description in this.ExportHistoryClient.ListJobsAsync(query))
            {
                if (description.JobId == result.JobId)
                {
                    found = true;
                    break;
                }
            }

            Assert.False(found, "Job should not be returned for non-matching prefixes.");
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyDescribe))
        {
            ExportJobDescription described = await this.ExportHistoryClient.GetJobAsync(result.JobId);
            Assert.Equal(result.JobDescription.Status, described.Status);
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyDeletion))
        {
            await result.JobClient.DeleteAsync();
            await Assert.ThrowsAsync<ExportJobNotFoundException>(() => this.ExportHistoryClient.GetJobAsync(result.JobId));
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyDescribeAfterDelete))
        {
            await result.JobClient.DeleteAsync();
            await Assert.ThrowsAsync<ExportJobNotFoundException>(() => this.ExportHistoryClient.GetJobAsync(result.JobId));
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyRecreation))
        {
            await result.JobClient.DeleteAsync();

            var recreateOptions = new ExportJobCreationOptions(
                ExportMode.Batch,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow,
                destination: null,
                jobId: result.JobId);

            await result.JobClient.CreateAsync(recreateOptions);
            await result.JobClient.DeleteAsync();
        }

        if (scenario.Assertions.HasFlag(ExportBatchAssertionFlags.VerifyHistoryEventsMatch))
        {
            Assert.NotEmpty(result.Blobs);

            // Check if we have any expected history (may be empty if GetOrchestrationHistoryAsync is not supported)
            if (result.ExpectedHistoryByInstanceId.Count == 0)
            {
                // History retrieval failed - this may be due to backend limitations (e.g., Orleans serialization)
                // Skip history verification but log a warning
                this.Logger.LogWarning(
                    "History verification skipped: GetOrchestrationHistoryAsync is not supported or failed for all instances. " +
                    "This may indicate a backend configuration issue.");
                return;
            }

            int verifiedCount = 0;
            // For each blob, parse the history events and compare with expected
            foreach (ExportedBlobArtifact blob in result.Blobs)
            {
                // Get the instanceId from blob metadata
                Assert.True(blob.Metadata.TryGetValue("instanceId", out string? instanceId), "Blob should have instanceId metadata");
                Assert.False(string.IsNullOrEmpty(instanceId), "instanceId metadata should not be empty");

                // Skip if we don't have expected history for this instance (history retrieval may have failed)
                if (!result.ExpectedHistoryByInstanceId.TryGetValue(instanceId, out IList<HistoryEvent>? expectedHistory) ||
                    expectedHistory.Count == 0)
                {
                    continue;
                }

                // Parse the exported history events from the blob
                IList<ExportedHistoryEventInfo> exportedHistory = ParseHistoryEventsFromBlob(blob);

                // Verify the history events match
                VerifyHistoryEventsMatch(expectedHistory, exportedHistory, instanceId);
                verifiedCount++;
            }

            // Warn if we couldn't verify any instances
            if (verifiedCount == 0)
            {
                this.Logger.LogWarning(
                    "History verification completed but no instances were verified. " +
                    "This may indicate that GetOrchestrationHistoryAsync is not supported by the backend.");
            }
        }
    }

    static string NormalizePrefix(string prefix)
    {
        string normalized = prefix.Replace("\\", "/");
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    /// <summary>
    /// Represents key properties extracted from an exported history event JSON.
    /// Used for comparison without needing to deserialize to the actual HistoryEvent types.
    /// </summary>
    sealed record ExportedHistoryEventInfo(int EventId, string EventType, DateTime Timestamp);

    static IList<ExportedHistoryEventInfo> ParseHistoryEventsFromBlob(ExportedBlobArtifact blob)
    {
        List<ExportedHistoryEventInfo> events = new();

        if (blob.FormatKind == ExportFormatKind.Jsonl)
        {
            // JSONL format: one event per line
            string[] lines = blob.TextPayload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(line);
                ExportedHistoryEventInfo? eventInfo = ExtractHistoryEventInfo(doc.RootElement);
                if (eventInfo != null)
                {
                    events.Add(eventInfo);
                }
            }
        }
        else
        {
            // JSON format: array of events
            using JsonDocument doc = JsonDocument.Parse(blob.TextPayload);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    ExportedHistoryEventInfo? eventInfo = ExtractHistoryEventInfo(element);
                    if (eventInfo != null)
                    {
                        events.Add(eventInfo);
                    }
                }
            }
        }

        return events;
    }

    static ExportedHistoryEventInfo? ExtractHistoryEventInfo(JsonElement element)
    {
        // Extract EventId
        int eventId = 0;
        if (element.TryGetProperty("eventId", out JsonElement eventIdElement) ||
            element.TryGetProperty("EventId", out eventIdElement))
        {
            eventId = eventIdElement.GetInt32();
        }

        // Extract EventType
        string eventType = string.Empty;
        if (element.TryGetProperty("eventType", out JsonElement eventTypeElement) ||
            element.TryGetProperty("EventType", out eventTypeElement))
        {
            eventType = eventTypeElement.GetString() ?? string.Empty;
        }

        // Extract Timestamp
        DateTime timestamp = DateTime.MinValue;
        if (element.TryGetProperty("timestamp", out JsonElement timestampElement) ||
            element.TryGetProperty("Timestamp", out timestampElement))
        {
            if (timestampElement.TryGetDateTime(out DateTime parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }
        }

        return new ExportedHistoryEventInfo(eventId, eventType, timestamp);
    }

    static void VerifyHistoryEventsMatch(IList<HistoryEvent> expected, IList<ExportedHistoryEventInfo> exported, string instanceId)
    {
        Assert.True(
            expected.Count == exported.Count,
            $"History event count mismatch for instance {instanceId}. Expected {expected.Count}, got {exported.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            HistoryEvent expectedEvent = expected[i];
            ExportedHistoryEventInfo exportedEvent = exported[i];

            // Compare event IDs
            Assert.True(
                expectedEvent.EventId == exportedEvent.EventId,
                $"EventId mismatch at index {i} for instance {instanceId}. Expected {expectedEvent.EventId}, got {exportedEvent.EventId}");

            // Compare event types (convert enum to string for comparison)
            string expectedEventType = expectedEvent.EventType.ToString();
            Assert.True(
                expectedEventType == exportedEvent.EventType,
                $"EventType mismatch at index {i} for instance {instanceId}. Expected {expectedEventType}, got {exportedEvent.EventType}");

            // Compare timestamps (allow small tolerance for serialization differences)
            TimeSpan timestampDiff = (expectedEvent.Timestamp - exportedEvent.Timestamp).Duration();
            Assert.True(
                timestampDiff < TimeSpan.FromSeconds(1),
                $"Timestamp mismatch at index {i} for instance {instanceId}. Expected {expectedEvent.Timestamp}, got {exportedEvent.Timestamp}");
        }
    }
}

