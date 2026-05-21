// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask.Client.AzureManaged;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

[ApiController]
[Route("")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("health")]
    public ActionResult<object> GetHealth() => this.Ok(new { status = "ok" });
}

[ApiController]
[Route("serverless/sandboxes")]
public sealed class ServerlessSandboxesController(
    ServerlessActivitiesClient client,
    ServerlessSandboxHttpOptions options) : ControllerBase
{
    readonly ServerlessActivitiesClient client = client;
    readonly ServerlessSandboxHttpOptions options = options;

    [HttpGet]
    public async Task<ActionResult<ServerlessSandboxListResponse>> ListSandboxes(
        [FromQuery] string? workerProfileId,
        CancellationToken cancellationToken)
    {
        try
        {
            string resolvedWorkerProfileId = string.IsNullOrWhiteSpace(workerProfileId)
                ? this.options.DefaultWorkerProfileId
                : workerProfileId;
            IReadOnlyList<ServerlessSandboxInfo> sandboxes = await this.client.ListServerlessActivitySandboxesAsync(
                resolvedWorkerProfileId,
                cancellationToken);
            ServerlessSandboxListResponse response = new(
                this.options.TaskHub,
                resolvedWorkerProfileId,
                sandboxes.Select(sandbox => new ServerlessSandboxSummary(
                    sandbox.DtsSandboxIdentifier,
                    sandbox.WorkerProfileId,
                    sandbox.State,
                    sandbox.CreatedAt == default ? null : sandbox.CreatedAt))
                .ToArray());

            return this.Ok(response);
        }
        catch (RpcException ex)
        {
            return ToGrpcProblem(ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return this.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("{dtsSandboxIdentifier}/logs")]
    public async Task StreamLogs(
        [FromRoute] string dtsSandboxIdentifier,
        [FromQuery] int? tail,
        CancellationToken cancellationToken)
    {
        this.Response.ContentType = "text/plain; charset=utf-8";
        try
        {
            int resolvedTail = Math.Clamp(tail ?? 100, 0, 300);
            await foreach (ServerlessSandboxLogLine line in this.client.StreamSandboxLogsAsync(
                dtsSandboxIdentifier,
                resolvedTail,
                cancellationToken))
            {
                await this.Response.WriteAsync(FormatLogLine(line), cancellationToken);
                await this.Response.WriteAsync(Environment.NewLine, cancellationToken);
                await this.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcException ex) when (!this.Response.HasStarted)
        {
            this.Response.StatusCode = StatusCodes.Status502BadGateway;
            await this.Response.WriteAsync($"DTS serverless log stream failed: {ex.Status.Detail}", cancellationToken);
        }
        catch (Exception ex) when ((ex is ArgumentException or InvalidOperationException) && !this.Response.HasStarted)
        {
            this.Response.StatusCode = StatusCodes.Status400BadRequest;
            await this.Response.WriteAsync(ex.Message, cancellationToken);
        }
    }

    ActionResult ToGrpcProblem(RpcException ex)
        => this.Problem(
            detail: ex.Status.Detail,
            statusCode: StatusCodes.Status502BadGateway,
            title: "DTS serverless gRPC call failed");

    static string FormatLogLine(ServerlessSandboxLogLine line)
    {
        if (!string.IsNullOrWhiteSpace(line.RawLine))
        {
            return line.RawLine;
        }

        string timestamp = line.Timestamp == default ? string.Empty : line.Timestamp.ToString("O");
        string stream = string.IsNullOrWhiteSpace(line.Stream) ? "log" : line.Stream;
        string tag = string.IsNullOrWhiteSpace(line.Tag) ? string.Empty : $"[{line.Tag}] ";
        return $"{timestamp} {stream}: {tag}{line.Message}".Trim();
    }
}
