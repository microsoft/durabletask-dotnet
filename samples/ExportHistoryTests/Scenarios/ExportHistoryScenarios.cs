using System.Linq;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;

namespace ExportHistoryTests.Scenarios;

[Flags]
public enum ExportBatchAssertionFlags
{
    None = 0,
    VerifyBlobContent = 1 << 0,
    VerifyBlobMetadata = 1 << 1,
    VerifyJobListing = 1 << 2,
    VerifyDescribe = 1 << 3,
    VerifyDeletion = 1 << 4,
    VerifyRecreation = 1 << 5,
    VerifyStatistics = 1 << 6,
    VerifyCheckpoint = 1 << 7,
    VerifyDefaultPrefix = 1 << 8,
    VerifyCustomPrefix = 1 << 9,
    VerifyContainer = 1 << 10,
    VerifyJsonCompression = 1 << 11,
    VerifyListFiltering = 1 << 12,
    VerifyDescribeAfterDelete = 1 << 13,
    VerifyBlobInstanceMetadata = 1 << 14,
    VerifyHistoryEventsMatch = 1 << 15,
}

public sealed record ExportHistoryBatchScenario(
    string Name,
    int CompletedInstances = 1,
    int FailedInstances = 0,
    int TerminatedInstances = 0,
    ExportFormatKind Format = ExportFormatKind.Jsonl,
    bool UseCustomContainer = false,
    string? CustomContainerSuffix = null,
    string? CustomPrefix = null,
    bool UseDefaultPrefix = false,
    bool UseFanOut = false,
    bool UseSubOrchestrator = false,
    bool UseLargePayload = false,
    int ActivityCount = 3,
    bool ExpectJobFailure = false,
    bool ForceMultipleBatches = false,
    int? MaxInstancesPerBatch = null,
    IReadOnlyList<OrchestrationRuntimeStatus>? RuntimeStatuses = null,
    bool KeepJobAlive = false,
    bool PreserveExportedBlobs = false,
    string? JobIdPrefix = null,
    ExportBatchAssertionFlags Assertions = ExportBatchAssertionFlags.None)
{
    public int TotalInstances => this.CompletedInstances + this.FailedInstances + this.TerminatedInstances;
}

[Flags]
public enum ExportContinuousAssertionFlags
{
    None = 0,
    VerifyActiveStatus = 1 << 0,
    VerifyCheckpointProgress = 1 << 1,
    VerifyBlobContent = 1 << 2,
    VerifyBlobMetadata = 1 << 3,
    VerifyStatistics = 1 << 4,
    VerifyJobListing = 1 << 5,
    VerifyDeletion = 1 << 6,
    VerifyHistoryEventsMatch = 1 << 7,
}

public sealed record ExportHistoryContinuousScenario(
    string Name,
    int InitialInstances = 2,
    int AdditionalInstances = 2,
    TimeSpan ObservationWindow = default,
    ExportFormatKind Format = ExportFormatKind.Jsonl,
    bool UseCustomContainer = false,
    string? CustomContainerSuffix = null,
    string? CustomPrefix = null,
    bool UseFanOut = false,
    bool UseSubOrchestrator = false,
    bool UseLargePayload = false,
    int ActivityCount = 3,
    bool KeepJobAlive = false,
    int ExpectedInitialExports = 2,
    int ExpectedTotalExports = 4,
    ExportContinuousAssertionFlags Assertions = ExportContinuousAssertionFlags.VerifyActiveStatus | ExportContinuousAssertionFlags.VerifyCheckpointProgress)
{
    public TimeSpan EffectiveObservationWindow => this.ObservationWindow == default
        ? TimeSpan.FromSeconds(45)
        : this.ObservationWindow;
}

public static class ExportHistoryScenarioData
{
    public static IEnumerable<object[]> BatchScenarioData =>
        BatchScenarios.Select(s => new object[] { s });

    public static IEnumerable<object[]> ContinuousScenarioData =>
        ContinuousScenarios.Select(s => new object[] { s });

    public static IReadOnlyList<ExportHistoryBatchScenario> BatchScenarios { get; } = BuildBatchScenarios();

    public static IReadOnlyList<ExportHistoryContinuousScenario> ContinuousScenarios { get; } = BuildContinuousScenarios();

    static IReadOnlyList<ExportHistoryBatchScenario> BuildBatchScenarios()
    {
        var scenarios = new List<ExportHistoryBatchScenario>
        {
            new("batch-json-default", CompletedInstances: 3, Format: ExportFormatKind.Json, Assertions: ExportBatchAssertionFlags.VerifyBlobContent | ExportBatchAssertionFlags.VerifyDefaultPrefix | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-default", Format: ExportFormatKind.Jsonl, Assertions: ExportBatchAssertionFlags.VerifyJsonCompression | ExportBatchAssertionFlags.VerifyBlobMetadata | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-custom-prefix", CompletedInstances: 3, CustomPrefix: "exports/custom-prefix/", Assertions: ExportBatchAssertionFlags.VerifyCustomPrefix | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-custom-container", CompletedInstances: 2, UseCustomContainer: true, CustomContainerSuffix: "alt", Assertions: ExportBatchAssertionFlags.VerifyContainer | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-fanout", CompletedInstances: 4, UseFanOut: true, ActivityCount: 5, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-sub-orch", CompletedInstances: 2, UseSubOrchestrator: true, Assertions: ExportBatchAssertionFlags.VerifyBlobContent | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-large-payload", CompletedInstances: 1, UseLargePayload: true, Assertions: ExportBatchAssertionFlags.VerifyBlobContent | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-runtime-completed", CompletedInstances: 3, FailedInstances: 2, RuntimeStatuses: new[] { OrchestrationRuntimeStatus.Completed }, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyCheckpoint | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-runtime-failed", CompletedInstances: 1, FailedInstances: 2, RuntimeStatuses: new[] { OrchestrationRuntimeStatus.Failed }, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-runtime-terminated", CompletedInstances: 1, TerminatedInstances: 2, RuntimeStatuses: new[] { OrchestrationRuntimeStatus.Terminated }, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-multi-batch", CompletedInstances: 6, ForceMultipleBatches: true, MaxInstancesPerBatch: 2, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyCheckpoint | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-list-query", CompletedInstances: 2, JobIdPrefix: "list-query", KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyJobListing | ExportBatchAssertionFlags.VerifyListFiltering),
            new("batch-describe", CompletedInstances: 2, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyDescribe),
            new("batch-delete", CompletedInstances: 2, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyDeletion),
            new("batch-recreate", CompletedInstances: 1, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyRecreation),
            new("batch-checkpoint", CompletedInstances: 5, ForceMultipleBatches: true, MaxInstancesPerBatch: 1, Assertions: ExportBatchAssertionFlags.VerifyCheckpoint | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-metadata", CompletedInstances: 2, Assertions: ExportBatchAssertionFlags.VerifyBlobMetadata | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-instance-metadata", CompletedInstances: 1, Assertions: ExportBatchAssertionFlags.VerifyBlobInstanceMetadata | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-default-prefix-validation", CompletedInstances: 1, UseDefaultPrefix: true, Assertions: ExportBatchAssertionFlags.VerifyDefaultPrefix | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-custom-prefix-validation", CompletedInstances: 2, CustomPrefix: "custom/validation/", Assertions: ExportBatchAssertionFlags.VerifyCustomPrefix | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            // Removed: batch-error-handling - Export jobs don't fail just because instances failed; they successfully export failed instances
            new("batch-many-completed", CompletedInstances: 8, ActivityCount: 2, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-many-failed", CompletedInstances: 2, FailedInstances: 4, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-many-terminated", CompletedInstances: 2, TerminatedInstances: 4, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-high-activity", CompletedInstances: 2, ActivityCount: 10, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-fanout-large", CompletedInstances: 5, UseFanOut: true, ActivityCount: 8, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-sub-and-fanout", CompletedInstances: 3, UseFanOut: true, UseSubOrchestrator: true, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-large-payload-fanout", CompletedInstances: 2, UseFanOut: true, UseLargePayload: true, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-long-activity", CompletedInstances: 2, ActivityCount: 6, Assertions: ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-json-metadata", CompletedInstances: 1, Format: ExportFormatKind.Json, Assertions: ExportBatchAssertionFlags.VerifyBlobMetadata | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-listing", CompletedInstances: 3, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyJobListing),
            new("batch-json-describe", CompletedInstances: 2, Format: ExportFormatKind.Json, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyDescribe),
            new("batch-json-delete", CompletedInstances: 1, Format: ExportFormatKind.Json, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyDeletion | ExportBatchAssertionFlags.VerifyDescribeAfterDelete),
            new("batch-json-recreate", CompletedInstances: 1, Format: ExportFormatKind.Json, KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyRecreation),
            new("batch-checkpoint-large", CompletedInstances: 9, ForceMultipleBatches: true, MaxInstancesPerBatch: 3, Assertions: ExportBatchAssertionFlags.VerifyCheckpoint | ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-prefix-jobid", CompletedInstances: 1, JobIdPrefix: "jobprefix", KeepJobAlive: true, Assertions: ExportBatchAssertionFlags.VerifyListFiltering),
            new("batch-container-prefix", CompletedInstances: 2, UseCustomContainer: true, CustomContainerSuffix: "pref", CustomPrefix: "container/prefix/", Assertions: ExportBatchAssertionFlags.VerifyContainer | ExportBatchAssertionFlags.VerifyCustomPrefix | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-compression", CompletedInstances: 2, Format: ExportFormatKind.Jsonl, Assertions: ExportBatchAssertionFlags.VerifyJsonCompression | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-jsonl-content", CompletedInstances: 2, Format: ExportFormatKind.Jsonl, Assertions: ExportBatchAssertionFlags.VerifyBlobContent | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-json-content", CompletedInstances: 2, Format: ExportFormatKind.Json, Assertions: ExportBatchAssertionFlags.VerifyBlobContent | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
            new("batch-json-stats", CompletedInstances: 5, Format: ExportFormatKind.Json, Assertions: ExportBatchAssertionFlags.VerifyStatistics | ExportBatchAssertionFlags.VerifyHistoryEventsMatch),
        };

        return scenarios;
    }

    static IReadOnlyList<ExportHistoryContinuousScenario> BuildContinuousScenarios()
    {
        var scenarios = new List<ExportHistoryContinuousScenario>
        {
            new("continuous-default", Assertions: ExportContinuousAssertionFlags.VerifyActiveStatus | ExportContinuousAssertionFlags.VerifyCheckpointProgress | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-custom-prefix", CustomPrefix: "continuous/custom/", Assertions: ExportContinuousAssertionFlags.VerifyBlobContent | ExportContinuousAssertionFlags.VerifyBlobMetadata | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-custom-container", UseCustomContainer: true, CustomContainerSuffix: "live", Assertions: ExportContinuousAssertionFlags.VerifyBlobMetadata | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-fanout", UseFanOut: true, ActivityCount: 5, ExpectedInitialExports: 3, ExpectedTotalExports: 6, Assertions: ExportContinuousAssertionFlags.VerifyActiveStatus | ExportContinuousAssertionFlags.VerifyCheckpointProgress | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-sub-orch", UseSubOrchestrator: true, ExpectedInitialExports: 2, ExpectedTotalExports: 4, Assertions: ExportContinuousAssertionFlags.VerifyActiveStatus | ExportContinuousAssertionFlags.VerifyCheckpointProgress | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-large-payload", UseLargePayload: true, Assertions: ExportContinuousAssertionFlags.VerifyBlobContent | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-job-listing", KeepJobAlive: true, Assertions: ExportContinuousAssertionFlags.VerifyJobListing),
            new("continuous-job-deletion", KeepJobAlive: true, Assertions: ExportContinuousAssertionFlags.VerifyDeletion),
            new("continuous-checkpoint", ExpectedInitialExports: 2, ExpectedTotalExports: 5, Assertions: ExportContinuousAssertionFlags.VerifyCheckpointProgress | ExportContinuousAssertionFlags.VerifyStatistics | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
            new("continuous-extended-window", ObservationWindow: TimeSpan.FromSeconds(90), ExpectedInitialExports: 3, ExpectedTotalExports: 6, Assertions: ExportContinuousAssertionFlags.VerifyActiveStatus | ExportContinuousAssertionFlags.VerifyCheckpointProgress | ExportContinuousAssertionFlags.VerifyHistoryEventsMatch),
        };

        return scenarios;
    }
}

