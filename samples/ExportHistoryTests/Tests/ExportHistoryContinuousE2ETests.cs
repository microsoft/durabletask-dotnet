using System.Linq;
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
    }
}

