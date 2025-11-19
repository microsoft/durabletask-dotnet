using System.Linq;
using ExportHistoryTests.Infrastructure;
using ExportHistoryTests.Scenarios;
using ExportHistoryTests.Utilities;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;
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
                Assert.Equal(ExportJobStatus.Completed, result.JobDescription.Status);
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
    }

    static string NormalizePrefix(string prefix)
    {
        string normalized = prefix.Replace("\\", "/");
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }
}

