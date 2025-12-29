// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DurableTask.Core.History;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.ExportHistory;

/// <summary>
/// Activity that exports one orchestration instance history to the configured blob destination.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ExportInstanceHistoryActivity"/> class.
/// </remarks>
[DurableTask]
public class ExportInstanceHistoryActivity(
    DurableTaskClient client,
    ILogger<ExportInstanceHistoryActivity> logger,
    IOptions<ExportHistoryStorageOptions> storageOptions) : TaskActivity<ExportRequest, ExportResult>
{
    readonly DurableTaskClient client = Check.NotNull(client, nameof(client));
    readonly ILogger<ExportInstanceHistoryActivity> logger = Check.NotNull(logger, nameof(logger));
    readonly ExportHistoryStorageOptions storageOptions = Check.NotNull(storageOptions?.Value, nameof(storageOptions));

    /// <inheritdoc/>
    public override async Task<ExportResult> RunAsync(TaskActivityContext context, ExportRequest input)
    {
        Check.NotNull(input, nameof(input));
        Check.NotNullOrEmpty(input.InstanceId, nameof(input.InstanceId));
        Check.NotNull(input.Destination, nameof(input.Destination));
        Check.NotNull(input.Format, nameof(input.Format));

        string instanceId = input.InstanceId;

        try
        {
            this.logger.LogInformation("Starting export for instance {InstanceId}", instanceId);

            // Get instance metadata with inputs and outputs
            OrchestrationMetadata? metadata = await this.client.GetInstanceAsync(
                instanceId,
                getInputsAndOutputs: true,
                cancellation: CancellationToken.None);

            if (metadata == null)
            {
                string error = $"Instance {instanceId} not found";
                this.logger.LogWarning(error);
                return new ExportResult
                {
                    InstanceId = instanceId,
                    Success = false,
                    Error = error,
                };
            }

            // Get completed timestamp (LastUpdatedAt for terminal states)
            DateTimeOffset completedTimestamp = metadata.LastUpdatedAt;
            if (!metadata.IsCompleted)
            {
                string error = $"Instance {instanceId} is not in a completed state";
                this.logger.LogWarning(error);
                return new ExportResult
                {
                    InstanceId = instanceId,
                    Success = false,
                    Error = error,
                };
            }

            // Stream all history events
            this.logger.LogInformation("Streaming history events for instance {InstanceId}", instanceId);
            IList<HistoryEvent> historyEvents = await this.client.GetOrchestrationHistoryAsync(instanceId, CancellationToken.None);

            this.logger.LogInformation(
                "Retrieved {EventCount} history events for instance {InstanceId}",
                historyEvents.Count,
                instanceId);

            // Create blob filename from hash of completed timestamp and instance ID
            string blobFileName = GenerateBlobFileName(completedTimestamp, instanceId, input.Format);

            // Build blob path with prefix if provided
            string blobPath = string.IsNullOrEmpty(input.Destination.Prefix)
                ? blobFileName
                : $"{input.Destination.Prefix.TrimEnd('/')}/{blobFileName}";

            // Serialize history events to JSON
            string jsonContent = SerializeInstanceData(historyEvents, input.Format);

            // Upload to blob storage
            await this.UploadToBlobStorageAsync(
                input.Destination.Container,
                blobPath,
                jsonContent,
                input.Format,
                instanceId,
                CancellationToken.None);

            this.logger.LogInformation(
                "Successfully exported instance {InstanceId} to blob {BlobPath}",
                instanceId,
                blobPath);

            return new ExportResult
            {
                InstanceId = instanceId,
                Success = true,
            };
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to export instance {InstanceId}", instanceId);
            return new ExportResult
            {
                InstanceId = instanceId,
                Success = false,
                Error = ex.Message,
            };
        }
    }

    static string GenerateBlobFileName(DateTimeOffset completedTimestamp, string instanceId, ExportFormat format)
    {
        // Create hash from completed timestamp and instance ID
        string hashInput = $"{completedTimestamp:O}|{instanceId}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Build filename with format extension
        string extension = GetFileExtension(format);

        return $"{hash}.{extension}";
    }

    /// <summary>
    /// Gets the file extension for a given export format.
    /// </summary>
    /// <param name="format">The export format.</param>
    /// <returns>The file extension (e.g., "json", "jsonl.gz").</returns>
    static string GetFileExtension(ExportFormat format)
    {
        return format.Kind switch
        {
            ExportFormatKind.Jsonl => "jsonl.gz",  // JSONL format is compressed
            ExportFormatKind.Json => "json",       // JSON format is uncompressed
            _ => "jsonl.gz",                       // Default to JSONL compressed
        };
    }

    static string SerializeInstanceData(
        IList<HistoryEvent> historyEvents,
        ExportFormat format)
    {
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true, // Include fields, not just properties (matches JsonDataConverter pattern)
            Converters = { new JsonStringEnumConverter() }, // Serialize enums as strings
        };

        if (format.Kind == ExportFormatKind.Jsonl)
        {
            // JSONL format: one history event per line
            // Serialize as object to preserve runtime type (polymorphic serialization)
            StringBuilder jsonlBuilder = new();

            foreach (HistoryEvent historyEvent in historyEvents)
            {
                // Serialize as object to preserve the actual derived type
                string json = JsonSerializer.Serialize((object)historyEvent, historyEvent.GetType(), serializerOptions);
                jsonlBuilder.AppendLine(json);
            }

            return jsonlBuilder.ToString();
        }
        else
        {
            // JSON format: array of history events
            // Convert to object array to preserve runtime types
            object[] eventsAsObjects = historyEvents.Cast<object>().ToArray();
            return JsonSerializer.Serialize(eventsAsObjects, serializerOptions);
        }
    }

    async Task UploadToBlobStorageAsync(
        string containerName,
        string blobPath,
        string content,
        ExportFormat format,
        string instanceId,
        CancellationToken cancellationToken)
    {
        // Create blob service client from connection string
        // Note: Azure.Storage.Blobs clients (BlobServiceClient, BlobContainerClient, BlobClient) are lightweight
        // wrappers that don't implement IDisposable/IAsyncDisposable. They use HttpClient internally which is
        // managed by the framework and doesn't require explicit disposal. The clients are designed to be
        // stateless and safe for reuse or short-lived usage without disposal.
        BlobServiceClient serviceClient = new(this.storageOptions.ConnectionString);
        BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(containerName);

        // Ensure container exists
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        // Get blob client
        BlobClient blobClient = containerClient.GetBlobClient(blobPath);

        // Upload content
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);

        if (format.Kind == ExportFormatKind.Jsonl)
        {
            // Compress with gzip
            using MemoryStream compressedStream = new();
            using (GZipStream gzipStream = new(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                await gzipStream.WriteAsync(contentBytes, cancellationToken);
                await gzipStream.FlushAsync(cancellationToken);
            }

            compressedStream.Position = 0;

            BlobUploadOptions uploadOptions = new()
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/jsonl+gzip",
                    ContentEncoding = "gzip",
                },
                Metadata = new Dictionary<string, string>
                {
                    { "instanceId", instanceId },
                },
            };

            await blobClient.UploadAsync(compressedStream, uploadOptions, cancellationToken);
        }
        else
        {
            // Upload uncompressed
            BlobUploadOptions uploadOptions = new()
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json",
                },
                Metadata = new Dictionary<string, string>
                {
                    { "instanceId", instanceId },
                },
            };

            await blobClient.UploadAsync(
                new BinaryData(contentBytes),
                uploadOptions,
                cancellationToken);
        }
    }
}

/// <summary>
/// Export request for one orchestration instance.
/// </summary>
public sealed class ExportRequest
{
    /// <summary>
    /// Gets or sets the instance ID to export.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the export destination configuration.
    /// </summary>
    public ExportDestination Destination { get; set; } = null!;

    /// <summary>
    /// Gets or sets the export format configuration.
    /// </summary>
    public ExportFormat Format { get; set; } = null!;
}

/// <summary>
/// Export result.
/// </summary>
public sealed class ExportResult
{
    /// <summary>
    /// Gets or sets the instance ID that was exported.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the export was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if the export failed.
    /// </summary>
    public string? Error { get; set; }
}
