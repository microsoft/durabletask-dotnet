using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DurableTask.Core.History;
using ExportHistoryTests.Infrastructure;
using ExportHistoryTests.Scenarios;
using ExportHistoryTests.Utilities;
using Microsoft.DurableTask.ExportHistory;
using Xunit;

namespace ExportHistoryTests.Tests;

public sealed class ExportHistoryContinuousE2ETests : ExportHistoryTestBase
{
    public ExportHistoryContinuousE2ETests(ExportHistoryTestFixture fixture)
        : base(fixture)
    {
    }

    [Theory]
    [MemberData(nameof(ExportHistoryScenarioData.ContinuousScenarioData), MemberType = typeof(ExportHistoryScenarioData))]
    public async Task ContinuousScenario_MaintainsProgress(ExportHistoryContinuousScenario scenario)
    {
        this.SkipIfNotConfigured();

        ExportScenarioResult result = await this.ScenarioRunner.ExecuteContinuousScenarioAsync(scenario);
        try
        {
            Assert.Equal(ExportJobStatus.Active, result.JobDescription.Status);
            Assert.True(result.JobDescription.ExportedInstances >= scenario.ExpectedInitialExports);

            await this.RunContinuousAssertionsAsync(scenario, result);
        }
        finally
        {
            if (result.RequiresManualCleanup)
            {
                await this.ScenarioRunner.CleanupAsync(result);
            }
        }
    }

    async Task RunContinuousAssertionsAsync(ExportHistoryContinuousScenario scenario, ExportScenarioResult result)
    {
        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyCheckpointProgress))
        {
            Assert.NotNull(result.JobDescription.Checkpoint);
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyBlobContent))
        {
            Assert.NotEmpty(result.Blobs);
            Assert.All(result.Blobs, blob => Assert.False(string.IsNullOrWhiteSpace(blob.TextPayload)));
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyBlobMetadata))
        {
            Assert.All(result.Blobs, blob => Assert.Contains("instanceId", blob.Metadata.Keys));
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyStatistics))
        {
            Assert.True(result.JobDescription.ScannedInstances >= scenario.ExpectedInitialExports);
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyJobListing))
        {
            ExportJobQuery query = new()
            {
                JobIdPrefix = result.JobId[..Math.Min(result.JobId.Length, 18)]
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

            Assert.True(found, "Continuous job should appear in listing.");
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyDeletion))
        {
            await result.JobClient.DeleteAsync();
            await Assert.ThrowsAsync<ExportJobNotFoundException>(() => this.ExportHistoryClient.GetJobAsync(result.JobId));
        }

        if (scenario.Assertions.HasFlag(ExportContinuousAssertionFlags.VerifyHistoryEventsMatch))
        {
            Assert.NotEmpty(result.Blobs);
            Assert.NotEmpty(result.ExpectedHistoryByInstanceId);

            // For each blob, parse the history events and compare with expected
            foreach (ExportedBlobArtifact blob in result.Blobs)
            {
                // Get the instanceId from blob metadata
                Assert.True(blob.Metadata.TryGetValue("instanceId", out string? instanceId), "Blob should have instanceId metadata");
                Assert.False(string.IsNullOrEmpty(instanceId), "instanceId metadata should not be empty");

                // Skip if we don't have expected history for this instance (it might be from a previous run)
                if (!result.ExpectedHistoryByInstanceId.TryGetValue(instanceId, out IList<HistoryEvent>? expectedHistory))
                {
                    continue;
                }

                // Parse the exported history events from the blob
                IList<ExportedHistoryEventInfo> exportedHistory = ParseHistoryEventsFromBlob(blob);

                // Verify the history events match
                VerifyHistoryEventsMatch(expectedHistory, exportedHistory, instanceId);
            }
        }
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

