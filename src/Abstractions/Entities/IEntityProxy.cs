// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Marker interface for entity proxy interfaces.
/// </summary>
/// <remarks>
/// This interface is used to mark interfaces that represent entity operations.
/// Entity proxy interfaces should define methods that correspond to operations
/// that can be invoked on entities. These interfaces are used with
/// <see cref="TaskOrchestrationEntityProxyExtensions"/> to create strongly-typed
/// proxies for entity invocation.
/// </remarks>
public interface IEntityProxy
{
}
