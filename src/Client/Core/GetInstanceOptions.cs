// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Options for fetching orchestration instance metadata.
/// </summary>
public sealed class GetInstanceOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to fetch the orchestration instance's input, output, and custom status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set to <c>true</c>, the <see cref="OrchestrationMetadata.SerializedInput"/>,
    /// <see cref="OrchestrationMetadata.SerializedOutput"/>, and <see cref="OrchestrationMetadata.SerializedCustomStatus"/>
    /// properties will be populated, and the <see cref="OrchestrationMetadata.ReadInputAs{T}"/>,
    /// <see cref="OrchestrationMetadata.ReadOutputAs{T}"/>, and <see cref="OrchestrationMetadata.ReadCustomStatusAs{T}"/>
    /// methods can be used.
    /// </para>
    /// <para>
    /// The default value is <c>false</c> to minimize the network bandwidth, serialization, and memory costs
    /// associated with fetching the instance metadata.
    /// </para>
    /// </remarks>
    /// <value><c>true</c> to fetch inputs and outputs; otherwise, <c>false</c>. The default is <c>false</c>.</value>
    public bool GetInputsAndOutputs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to fetch the orchestration instance's execution history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set to <c>true</c>, the <see cref="OrchestrationMetadata.History"/> property will be populated
    /// with the complete history of events for the orchestration instance.
    /// </para>
    /// <para>
    /// Fetching history can be expensive for long-running orchestrations with many events.
    /// Use this option only when you need to inspect the detailed execution history.
    /// </para>
    /// <para>
    /// Note: Not all backend implementations support fetching history. If the backend does not support
    /// history retrieval, this option may be ignored or may throw a <see cref="NotSupportedException"/>.
    /// </para>
    /// </remarks>
    /// <value><c>true</c> to fetch the execution history; otherwise, <c>false</c>. The default is <c>false</c>.</value>
    public bool GetHistory { get; set; }
}
