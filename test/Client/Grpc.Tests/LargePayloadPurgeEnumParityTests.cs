// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

/// <summary>
/// <see cref="GrpcDurableTaskClient.ReportLargePayloadPurgeResultsAsync"/> maps the managed purge enums onto
/// their protobuf counterparts by numeric value rather than by name, which is only correct while the two sides
/// agree on every value. A silent drift would not fail to compile; it would send the backend a different
/// disposition than the worker decided and delete or quarantine the wrong rows. These tests pin the mapping.
/// </summary>
public class LargePayloadPurgeEnumParityTests
{
    [Fact]
    public void Disposition_ManagedAndProtobufValues_AreIdentical()
    {
        // Arrange & Act
        Dictionary<int, string> managed = Enum.GetValues(typeof(LargePayloadPurgeDisposition))
            .Cast<LargePayloadPurgeDisposition>()
            .ToDictionary(v => (int)v, v => v.ToString());
        Dictionary<int, string> proto = Enum.GetValues(typeof(P.LargePayloadPurgeDisposition))
            .Cast<P.LargePayloadPurgeDisposition>()
            .ToDictionary(v => (int)v, v => v.ToString());

        // Assert - same numeric values AND the same names at each value, so neither side can gain, lose, or
        // renumber a member unnoticed.
        managed.Should().Equal(proto);
    }

    [Fact]
    public void Reason_ManagedAndProtobufValues_AreIdentical()
    {
        // Arrange & Act
        Dictionary<int, string> managed = Enum.GetValues(typeof(LargePayloadPurgeReason))
            .Cast<LargePayloadPurgeReason>()
            .ToDictionary(v => (int)v, v => v.ToString());
        Dictionary<int, string> proto = Enum.GetValues(typeof(P.LargePayloadPurgeReason))
            .Cast<P.LargePayloadPurgeReason>()
            .ToDictionary(v => (int)v, v => v.ToString());

        // Assert
        managed.Should().Equal(proto);
    }
}
