// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using P = DurableTask.Protobuf;

namespace DurableTask;

public sealed class OrchestrationMetadata
{
    readonly IDataConverter dataConverter;
    readonly bool requestedInputsAndOutputs;

    internal OrchestrationMetadata(
        P.GetInstanceResponse response,
        IDataConverter dataConverter,
        bool requestedInputsAndOutputs)
    {
        this.InstanceId = response.OrchestrationState.InstanceId;
        this.RuntimeStatus = (OrchestrationRuntimeStatus)response.OrchestrationState.OrchestrationStatus;
        this.CreatedAt = response.OrchestrationState.CreatedTimestamp.ToDateTimeOffset();
        this.LastUpdatedAt = response.OrchestrationState.LastUpdatedTimestamp.ToDateTimeOffset();
        this.SerializedInput = response.OrchestrationState.Input;
        this.SerializedOutput = response.OrchestrationState.Output;
        this.SerializedCustomStatus = response.OrchestrationState.CustomStatus;
        this.dataConverter = dataConverter;
        this.requestedInputsAndOutputs = requestedInputsAndOutputs;
    }

    public string InstanceId { get; }

    public OrchestrationRuntimeStatus RuntimeStatus { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastUpdatedAt { get; }

    public string? SerializedInput { get; }

    public string? SerializedOutput { get; }
    
    public string? SerializedCustomStatus { get; }

    public bool IsRunning =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Running;

    public bool IsCompleted =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Terminated;

    public T? ReadInputAs<T>()
    {
        if (!this.requestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadInputAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include input data.");
        }

        return this.dataConverter.Deserialize<T>(this.SerializedInput);
    }

    public T? ReadOutputAs<T>()
    {
        if (!this.requestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadOutputAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include output data.");
        }

        return this.dataConverter.Deserialize<T>(this.SerializedOutput);
    }

    public T? ReadCustomStatusAs<T>()
    {
        if (!this.requestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadCustomStatusAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include input and output data.");
        }

        return this.dataConverter.Deserialize<T>(this.SerializedCustomStatus);
    }

    public override string ToString()
    {
        StringBuilder sb = new($"[ID: '{this.InstanceId}', RuntimeStatus: {this.RuntimeStatus}, CreatedAt: {this.CreatedAt:s}, LastUpdatedAt: {this.LastUpdatedAt:s}");
        if (this.SerializedInput != null)
        {
            sb.Append(", Input: '").Append(GetTrimmedPayload(this.SerializedInput)).Append('\'');
        }

        if (this.SerializedOutput != null)
        {
            sb.Append(", Output: '").Append(GetTrimmedPayload(this.SerializedOutput)).Append('\'');
        }

        return sb.Append(']').ToString();
    }

    static string GetTrimmedPayload(string payload)
    {
        const int MaxLength = 50;
        if (payload.Length > MaxLength)
        {
            return string.Concat(payload.AsSpan(0, MaxLength), "...");
        }

        return payload;
    }
}
