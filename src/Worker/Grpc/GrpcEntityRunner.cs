﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Google.Protobuf;
using Microsoft.DurableTask.Entities;
using Microsoft.DurableTask.Worker.Shims;
using Microsoft.Extensions.DependencyInjection;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Helper class for invoking entities directly, without building a worker instance.
/// </summary>
/// <remarks>
/// <para>
/// This static class can be used to execute entity logic directly. In order to use it for this purpose, the caller must
/// provider entity state as a serialized protobuf bytes.
/// </para>
/// <para>
/// The Azure Functions .NET worker extension is the primary intended user of this class, where entity state is provided
/// by trigger bindings.
/// </para>
/// </remarks>
public static class GrpcEntityRunner
{
    /// <summary>
    /// Deserializes entity batch request from <paramref name="encodedEntityRequest"/> and uses it to invoke the
    /// requested operations implemented by <paramref name="implementation"/>.
    /// </summary>
    /// <param name="encodedEntityRequest">
    /// The encoded protobuf payload representing an entity batch request. This is a base64-encoded string.
    /// </param>
    /// <param name="implementation">
    /// An <see cref="ITaskEntity"/> implementation that defines the entity logic.
    /// </param>
    /// <param name="services">
    /// Optional <see cref="IServiceProvider"/> from which injected dependencies can be retrieved.
    /// </param>
    /// <returns>
    /// Returns a serialized result of the entity batch that should be used as the return value of the entity function
    /// trigger.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="encodedEntityRequest"/> or <paramref name="implementation"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="encodedEntityRequest"/> contains invalid data.
    /// </exception>
    public static async Task<string> LoadAndRunAsync(
        string encodedEntityRequest, ITaskEntity implementation, IServiceProvider? services = null)
    {
        Check.NotNullOrEmpty(encodedEntityRequest);
        Check.NotNull(implementation);

        P.EntityBatchRequest request = P.EntityBatchRequest.Parser.Base64Decode<P.EntityBatchRequest>(
            encodedEntityRequest);

        EntityBatchRequest batch = request.ToEntityBatchRequest();
        EntityId id = EntityId.FromString(batch.InstanceId!);
        TaskName entityName = new(id.Name);

        DurableTaskShimFactory factory = services is null
            ? DurableTaskShimFactory.Default
            : ActivatorUtilities.GetServiceOrCreateInstance<DurableTaskShimFactory>(services);

        TaskEntity entity = factory.CreateEntity(entityName, implementation, id);
        EntityBatchResult result = await entity.ExecuteOperationBatchAsync(batch);

        P.EntityBatchResult response = result.ToEntityBatchResult();
        byte[] responseBytes = response.ToByteArray();
        return Convert.ToBase64String(responseBytes);
    }
}
