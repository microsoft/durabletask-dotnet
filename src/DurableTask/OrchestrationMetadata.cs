//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Text;
using P = DurableTask.Protobuf;

namespace DurableTask;

public sealed class OrchestrationMetadata
{
    readonly IDataConverter dataConverter;

    internal OrchestrationMetadata(P.GetInstanceResponse response, IDataConverter dataConverter)
    {
        this.InstanceId = response.InstanceId;
        this.RuntimeStatus = (OrchestrationRuntimeStatus)response.OrchestrationStatus;
        this.CreatedAt = response.CreatedTimestamp.ToDateTimeOffset();
        this.LastUpdatedAt = response.LastUpdatedTimestamp.ToDateTimeOffset();
        this.SerializedInput = response.Input;
        this.SerializedOutput = response.Output;
        this.dataConverter = dataConverter;
    }

    public string InstanceId { get; }

    public OrchestrationRuntimeStatus RuntimeStatus { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastUpdatedAt { get; }

    public string? SerializedInput { get; }

    public string? SerializedOutput { get; }

    public bool IsRunning =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Running;

    public bool IsCompleted =>
        this.RuntimeStatus == OrchestrationRuntimeStatus.Completed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
        this.RuntimeStatus == OrchestrationRuntimeStatus.Terminated;

    public T? ReadInputAs<T>() => this.dataConverter.Deserialize<T>(this.SerializedInput);

    public T? ReadOutputAs<T>() => this.dataConverter.Deserialize<T>(this.SerializedOutput);

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
