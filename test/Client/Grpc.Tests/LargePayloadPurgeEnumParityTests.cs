// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Client;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Client.Grpc.Tests;

/// <summary>
/// <see cref="GrpcDurableTaskClient.ReportLargePayloadPurgeResultsAsync"/> maps the managed purge disposition
/// onto its protobuf counterpart by numeric value rather than by name, which is only correct while the two
/// sides agree on every value. A silent drift would not fail to compile; it would send the backend a different
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

    /// <summary>
    /// The numeric cast is safe only because no enum crosses the wire <i>inbound</i> on this feature: the SDK
    /// casts a value it defined itself, so it can never receive an unknown value and silently reinterpret it.
    /// That invariant holds today by the shape of the contract, not by construction, and nothing in the code
    /// states it. Adding an enum to an inbound type would create exactly that path - a newer backend sending a
    /// value this SDK does not know, mapped by raw numeric cast onto a valid-but-wrong member - and it would
    /// compile silently. This pins the invariant so it fails here instead.
    /// </summary>
    /// <param name="inboundType">A type carrying server-to-client data for the purge feature.</param>
    [Theory]
    [InlineData(typeof(LargePayloadTombstone))]
    [InlineData(typeof(P.LargePayloadTombstone))]
    [InlineData(typeof(P.GetLargePayloadTombstonesResponse))]
    [InlineData(typeof(P.ReportLargePayloadPurgeResultsResponse))]
    public void InboundTypes_ExposeNoEnumMembers(Type inboundType)
    {
        // Arrange & Act
        List<string> enumMembers = inboundType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).IsEnum)
            .Select(p => $"{inboundType.Name}.{p.Name}")
            .ToList();

        // Assert
        enumMembers.Should().BeEmpty(
            "an enum on an inbound type invalidates the numeric enum cast in " +
            "GrpcDurableTaskClient.ReportLargePayloadPurgeResultsAsync. The SDK would map a value chosen by the " +
            "backend - including one a newer backend added that this SDK does not know - onto a managed member " +
            "by raw numeric value, silently mis-dispositioning rows. Map inbound enums explicitly instead, with " +
            "a switch that handles unknown values");
    }
}
