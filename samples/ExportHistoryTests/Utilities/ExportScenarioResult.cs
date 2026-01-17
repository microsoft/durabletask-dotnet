using DurableTask.Core.History;
using ExportHistoryTests.Scenarios;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;

namespace ExportHistoryTests.Utilities;

public sealed record ExportInstanceRecord(
    string InstanceId,
    OrchestrationRuntimeStatus Status,
    DateTimeOffset CompletedTimeUtc);

public sealed record ExportedBlobArtifact(
    string ContainerName,
    string BlobName,
    string ContentType,
    long ContentLength,
    IReadOnlyDictionary<string, string> Metadata,
    string TextPayload,
    bool IsCompressed,
    ExportFormatKind FormatKind);

public sealed record ExportScenarioResult(
    string JobId,
    ExportMode Mode,
    ExportJobDescription JobDescription,
    IReadOnlyList<ExportInstanceRecord> Instances,
    IReadOnlyList<ExportedBlobArtifact> Blobs,
    string ContainerName,
    string Prefix,
    ExportHistoryBatchScenario? BatchScenario,
    ExportHistoryContinuousScenario? ContinuousScenario,
    bool RequiresManualCleanup,
    ExportHistoryJobClient JobClient,
    IReadOnlyList<string> InstanceIdsToPurge,
    IReadOnlyDictionary<string, IList<HistoryEvent>> ExpectedHistoryByInstanceId)
{
    public bool IsBatch => this.Mode == ExportMode.Batch;
}

