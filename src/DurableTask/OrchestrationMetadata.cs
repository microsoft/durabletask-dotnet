// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using Microsoft.DurableTask.Grpc;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask;

/// <summary>
/// Represents a snapshot of an orchestration instance's current state, including metadata.
/// </summary>
/// <remarks>
/// Instances of this class are produced by methods in the <see cref="DurableTaskClient"/> class, such as
/// <see cref="DurableTaskClient.GetInstanceMetadataAsync"/>,
/// <see cref="DurableTaskClient.WaitForInstanceStartAsync"/> and
/// <see cref="DurableTaskClient.WaitForInstanceCompletionAsync"/>.
/// </remarks>
public sealed class OrchestrationMetadata
{
    readonly DataConverter dataConverter;
    readonly bool requestedInputsAndOutputs;

    internal OrchestrationMetadata(
        P.GetInstanceResponse response,
        DataConverter dataConverter,
        bool requestedInputsAndOutputs)
    {
        this.Name = response.OrchestrationState.Name;
        this.InstanceId = response.OrchestrationState.InstanceId;
        this.RuntimeStatus = (OrchestrationRuntimeStatus)response.OrchestrationState.OrchestrationStatus;
        this.CreatedAt = response.OrchestrationState.CreatedTimestamp.ToDateTimeOffset();
        this.LastUpdatedAt = response.OrchestrationState.LastUpdatedTimestamp.ToDateTimeOffset();
        this.SerializedInput = response.OrchestrationState.Input;
        this.SerializedOutput = response.OrchestrationState.Output;
        this.SerializedCustomStatus = response.OrchestrationState.CustomStatus;
        this.FailureDetails = ProtoUtils.ConvertTaskFailureDetails(response.OrchestrationState?.FailureDetails);
        this.dataConverter = dataConverter;
        this.requestedInputsAndOutputs = requestedInputsAndOutputs;
    }

    /// <summary>
    /// Gets the name of the orchestration.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the unique ID of the orchestration instance.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the current runtime status of the orchestration instance at the time this object was fetched.
    /// </summary>
    public OrchestrationRuntimeStatus RuntimeStatus { get; }

    /// <summary>
    /// Gets the orchestration instance's creation time in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the orchestration instance's last updated time in UTC.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; }

    /// <summary>
    /// Gets the orchestration instance's serialized input, if any, as a string value.
    /// </summary>
    public string? SerializedInput { get; }

    /// <summary>
    /// Gets the orchestration instance's serialized output, if any, as a string value.
    /// </summary>
    public string? SerializedOutput { get; }

    /// <summary>
    /// Gets the orchestration instance's serialized custom status, if any, as a string value.
    /// </summary>
    public string? SerializedCustomStatus { get; }

    /// <summary>
    /// Gets the failure details, if any, for the orchestration instance.
    /// </summary>
    /// <remarks>
    /// This property contains data only if the orchestration is in the <see cref="OrchestrationRuntimeStatus.Failed"/>
    /// state, and only if this instance metadata was fetched with the option to include output data.
    /// </remarks>
    public TaskFailureDetails? FailureDetails { get; }

    /// <summary>
    /// Gets a value indicating whether the orchestration instance was running at the time this object was fetched.
    /// </summary>
    public bool IsRunning => this.RuntimeStatus == OrchestrationRuntimeStatus.Running;

    /// <summary>
    /// Gets a value indicating whether the orchestration instance was completed at the time this object was fetched.
    /// </summary>
    /// <remarks>
    /// An orchestration instance is considered completed when its <see cref="RuntimeStatus"/> value is
    /// <see cref="OrchestrationRuntimeStatus.Completed"/>, <see cref="OrchestrationRuntimeStatus.Failed"/>,
    /// or <see cref="OrchestrationRuntimeStatus.Terminated"/>.
    /// </remarks>
    public bool IsCompleted =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Terminated;

    /// <summary>
    /// Deserializes the orchestration's input into an object of the specified type.
    /// </summary>
    /// <remarks>
    /// This method can only be used when inputs and outputs are explicitly requested from the 
    /// <see cref="DurableTaskClient.GetInstanceMetadataAsync"/> or 
    /// <see cref="DurableTaskClient.WaitForInstanceCompletionAsync"/> method that produced this
    /// <see cref="OrchestrationMetadata"/> object.
    /// </remarks>
    /// <typeparam name="T">The type to deserialize the orchestration input into.</typeparam>
    /// <returns>Returns the deserialized input value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this metadata object was fetched without the option to read inputs and outputs.
    /// </exception>
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

    /// <summary>
    /// Deserializes the orchestration's output into an object of the specified type.
    /// </summary>
    /// <remarks>
    /// This method can only be used when inputs and outputs are explicitly requested from the 
    /// <see cref="DurableTaskClient.GetInstanceMetadataAsync"/> or 
    /// <see cref="DurableTaskClient.WaitForInstanceCompletionAsync"/> method that produced this
    /// <see cref="OrchestrationMetadata"/> object.
    /// </remarks>
    /// <typeparam name="T">The type to deserialize the orchestration output into.</typeparam>
    /// <returns>Returns the deserialized output value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this metadata object was fetched without the option to read inputs and outputs.
    /// </exception>
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

    /// <summary>
    /// Deserializes the orchestration's custom status value into an object of the specified type.
    /// </summary>
    /// <remarks>
    /// This method can only be used when inputs and outputs are explicitly requested from the 
    /// <see cref="DurableTaskClient.GetInstanceMetadataAsync"/> or 
    /// <see cref="DurableTaskClient.WaitForInstanceCompletionAsync"/> method that produced this
    /// <see cref="OrchestrationMetadata"/> object.
    /// </remarks>
    /// <typeparam name="T">The type to deserialize the orchestration' custom status into.</typeparam>
    /// <returns>Returns the deserialized custom status value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if this metadata object was fetched without the option to read inputs and outputs.
    /// </exception>
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

    /// <summary>
    /// Generates a user-friendly string representation of the current metadata object.
    /// </summary>
    /// <returns>A user-friendly string representation of the current metadata object.</returns>
    public override string ToString()
    {
        StringBuilder sb = new($"[Name: '{this.Name}', ID: '{this.InstanceId}', RuntimeStatus: {this.RuntimeStatus}, CreatedAt: {this.CreatedAt:s}, LastUpdatedAt: {this.LastUpdatedAt:s}");
        if (this.SerializedInput != null)
        {
            sb.Append(", Input: '").Append(GetTrimmedPayload(this.SerializedInput)).Append('\'');
        }

        if (this.SerializedOutput != null)
        {
            sb.Append(", Output: '").Append(GetTrimmedPayload(this.SerializedOutput)).Append('\'');
        }

        if (this.FailureDetails != null)
        {
            sb.Append(", FailureDetails: '")
                .Append(this.FailureDetails.ErrorType)
                .Append(" - ")
                .Append(GetTrimmedPayload(this.FailureDetails.ErrorMessage))
                .Append('\'');
        }

        return sb.Append(']').ToString();
    }

    static string GetTrimmedPayload(string payload)
    {
        const int MaxLength = 50;
        if (payload.Length > MaxLength)
        {
            return string.Concat(payload[..MaxLength], "...");
        }

        return payload;
    }
}
