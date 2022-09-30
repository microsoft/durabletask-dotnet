// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text;

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
    /// <summary>
    /// Initializes a new instance of <see cref="OrchestrationMetadata" />,
    /// </summary>
    /// <param name="name">The name of the orchestration.</param>
    /// <param name="instanceId">The instance ID of the orchestration.</param>
    public OrchestrationMetadata(string name, string instanceId)
    {
        this.Name = name;
        this.InstanceId = instanceId;
    }

    /// <summary>Gets the name of the orchestration.</summary>
    /// <value>The name of the orchestration.</value>
    public string Name { get; }

    /// <summary>Gets the unique ID of the orchestration instance.</summary>
    /// <value>The unique ID of the orchestration instance.</value>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the data converter used to deserialized the serialized data on this instance.
    /// This will only be present when inputs and outputs are requested, <c>null</c> otherwise.
    /// </summary>
    /// <value>The optional data converter.</value>
    public DataConverter? DataConverter { get; init; }

    /// <summary>
    /// Gets the current runtime status of the orchestration instance at the time this object was fetched.
    /// </summary>
    /// <value>The runtime status of the orchestration instance at the time this object was fetched</value>
    public OrchestrationRuntimeStatus RuntimeStatus { get; init; }

    /// <summary>
    /// Gets the orchestration instance's creation time in UTC.
    /// </summary>
    /// <value>The orchestration instance's creation time in UTC.</value>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the orchestration instance's last updated time in UTC.
    /// </summary>
    /// <value>The orchestration instance's last updated time in UTC.</value>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Gets the orchestration instance's serialized input, if any, as a string value.
    /// </summary>
    /// <value>The serialized orchestration input or <c>null</c>.</value>
    public string? SerializedInput { get; init; }

    /// <summary>
    /// Gets the orchestration instance's serialized output, if any, as a string value.
    /// </summary>
    /// <value>The serialized orchestration output or <c>null</c>.</value>
    public string? SerializedOutput { get; init; }

    /// <summary>
    /// Gets the orchestration instance's serialized custom status, if any, as a string value.
    /// </summary>
    /// <value>The serialized custom status or <c>null</c>.</value>
    public string? SerializedCustomStatus { get; init; }

    /// <summary>
    /// Gets the failure details, if any, for the orchestration instance.
    /// </summary>
    /// <remarks>
    /// This property contains data only if the orchestration is in the <see cref="OrchestrationRuntimeStatus.Failed"/>
    /// state, and only if this instance metadata was fetched with the option to include output data.
    /// </remarks>
    /// <value>The failure details if the orchestration was in a failed state; <c>null</c> otherwise.</value>
    public TaskFailureDetails? FailureDetails { get; init; }

    /// <summary>
    /// Gets a value indicating whether the orchestration instance was running at the time this object was fetched.
    /// </summary>
    /// <value><c>true</c> if the orchestration was in a running state; <c>false</c> otherwise.</value>
    public bool IsRunning => this.RuntimeStatus == OrchestrationRuntimeStatus.Running;

    /// <summary>
    /// Gets a value indicating whether the orchestration instance was completed at the time this object was fetched.
    /// </summary>
    /// <remarks>
    /// An orchestration instance is considered completed when its <see cref="RuntimeStatus"/> value is
    /// <see cref="OrchestrationRuntimeStatus.Completed"/>, <see cref="OrchestrationRuntimeStatus.Failed"/>,
    /// or <see cref="OrchestrationRuntimeStatus.Terminated"/>.
    /// </remarks>
    /// <value><c>true</c> if the orchestration was in a terminal state; <c>false</c> otherwise.</value>
    public bool IsCompleted =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Terminated;

    [MemberNotNullWhen(true, nameof(DataConverter))]
    bool RequestedInputsAndOutputs => this.DataConverter is not null;

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
        if (!this.RequestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadInputAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include input data.");
        }

        return this.DataConverter.Deserialize<T>(this.SerializedInput);
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
        if (!this.RequestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadOutputAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include output data.");
        }

        return this.DataConverter.Deserialize<T>(this.SerializedOutput);
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
        if (!this.RequestedInputsAndOutputs)
        {
            throw new InvalidOperationException(
                $"The {nameof(this.ReadCustomStatusAs)} method can only be used on {nameof(OrchestrationMetadata)} objects " +
                "that are fetched with the option to include input and output data.");
        }

        return this.DataConverter.Deserialize<T>(this.SerializedCustomStatus);
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
