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
[DurableTask]
public class ExportInstanceHistoryActivity : TaskActivity<ExportRequest, ExportResult>
{
    readonly IDurableTaskClientProvider clientProvider;
    readonly ILogger<ExportInstanceHistoryActivity> logger;
    readonly ExportHistoryStorageOptions storageOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportInstanceHistoryActivity"/> class.
    /// </summary>
    public ExportInstanceHistoryActivity(
        IDurableTaskClientProvider clientProvider,
        ILogger<ExportInstanceHistoryActivity> logger,
        IOptions<ExportHistoryStorageOptions> storageOptions)
    {
        this.clientProvider = Check.NotNull(clientProvider, nameof(clientProvider));
        this.logger = Check.NotNull(logger, nameof(logger));
        this.storageOptions = Check.NotNull(storageOptions?.Value, nameof(storageOptions));
    }

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

            // Get the client and instance metadata with inputs and outputs
            DurableTaskClient client = this.clientProvider.GetClient();

            OrchestrationMetadata? metadata = await client.GetInstanceAsync(
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
            List<HistoryEvent> historyEvents = new();
            await foreach (HistoryEvent historyEvent in client.StreamInstanceHistoryAsync(
                instanceId,
                executionId: null, // Use latest execution
                cancellation: CancellationToken.None))
            {
                historyEvents.Add(historyEvent);
            }

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
        string formatKind = format.Kind.ToLowerInvariant();

        return formatKind switch
        {
            "jsonl" => "jsonl.gz",  // JSONL format is compressed
            "json" => "json",       // JSON format is uncompressed
            _ => "jsonl.gz",        // Default to JSONL compressed
        };
    }

    static string SerializeInstanceData(
        List<HistoryEvent> historyEvents,
        ExportFormat format)
    {
        string formatKind = format.Kind.ToLowerInvariant();
        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        if (formatKind == "jsonl")
        {
            // JSONL format: one history event per line
            StringBuilder jsonlBuilder = new();

            foreach (HistoryEvent historyEvent in historyEvents)
            {
                jsonlBuilder.AppendLine(JsonSerializer.Serialize(historyEvent, serializerOptions));
            }

            return jsonlBuilder.ToString();
        }
        else
        {
            // JSON format: array of history events
            return JsonSerializer.Serialize(historyEvents, serializerOptions);
        }
    }

    async Task UploadToBlobStorageAsync(
        string containerName,
        string blobPath,
        string content,
        ExportFormat format,
        CancellationToken cancellationToken)
    {
        // Create blob service client from connection string
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

        if (format.Kind.ToLowerInvariant() == "jsonl")
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
