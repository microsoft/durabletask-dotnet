// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Microsoft.DurableTask.Entities;

namespace Microsoft.DurableTask.Client.Entities;

/// <summary>
/// Extension methods for creating strongly-typed entity proxies on the client side.
/// </summary>
public static class DurableEntityClientProxyExtensions
{
    /// <summary>
    /// Creates a strongly-typed proxy for invoking entity operations from a client.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type. Must extend <see cref="IEntityProxy"/>.</typeparam>
    /// <param name="client">The durable entity client.</param>
    /// <param name="id">The entity instance ID.</param>
    /// <returns>A strongly-typed proxy for the entity.</returns>
    /// <remarks>
    /// <para>
    /// The proxy interface should define methods that correspond to entity operations.
    /// All method invocations will use SignalEntityAsync (fire-and-forget) since clients
    /// cannot wait for entity operation results.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// public interface ICounter : IEntityProxy
    /// {
    ///     Task Add(int value);
    ///     Task Reset();
    /// }
    ///
    /// var counter = client.Entities.CreateProxy&lt;ICounter&gt;(new EntityInstanceId("Counter", "myCounter"));
    /// await counter.Add(5);
    /// </code>
    /// </para>
    /// </remarks>
    public static TEntityProxy CreateProxy<TEntityProxy>(
        this DurableEntityClient client,
        EntityInstanceId id)
        where TEntityProxy : class, IEntityProxy
    {
        Check.NotNull(client);
        return EntityClientProxy<TEntityProxy>.Create(client, id);
    }

    /// <summary>
    /// Creates a strongly-typed proxy for invoking entity operations from a client.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type. Must extend <see cref="IEntityProxy"/>.</typeparam>
    /// <param name="client">The durable entity client.</param>
    /// <param name="entityName">The entity name.</param>
    /// <param name="entityKey">The entity key.</param>
    /// <returns>A strongly-typed proxy for the entity.</returns>
    public static TEntityProxy CreateProxy<TEntityProxy>(
        this DurableEntityClient client,
        string entityName,
        string entityKey)
        where TEntityProxy : class, IEntityProxy
    {
        return CreateProxy<TEntityProxy>(client, new EntityInstanceId(entityName, entityKey));
    }

    /// <summary>
    /// Proxy implementation for client-side entity invocation.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type.</typeparam>
    class EntityClientProxy<TEntityProxy> : DispatchProxy
        where TEntityProxy : class, IEntityProxy
    {
        DurableEntityClient client = null!;
        EntityInstanceId id;

        /// <summary>
        /// Creates a proxy instance.
        /// </summary>
        /// <param name="entityClient">The durable entity client.</param>
        /// <param name="entityId">The entity instance ID.</param>
        /// <returns>The proxy instance.</returns>
        public static TEntityProxy Create(DurableEntityClient entityClient, EntityInstanceId entityId)
        {
            object proxy = Create<TEntityProxy, EntityClientProxy<TEntityProxy>>();
            ((EntityClientProxy<TEntityProxy>)proxy).Initialize(entityClient, entityId);
            return (TEntityProxy)proxy;
        }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new ArgumentNullException(nameof(targetMethod));
            }

            // Get the operation name from the method name
            string operationName = targetMethod.Name;

            // Determine input - if there's exactly one parameter, use it; otherwise use args array or null
            object? input = args?.Length switch
            {
                0 => null,
                1 => args[0],
                _ => args,
            };

            Type returnType = targetMethod.ReturnType;

            // Client proxies can only signal entities (fire-and-forget)
            // They cannot wait for results since clients don't have orchestration context

            // Handle void methods
            if (returnType == typeof(void))
            {
                Task signalTask = this.client.SignalEntityAsync(this.id, operationName, input);
                signalTask.ConfigureAwait(false).GetAwaiter().GetResult();
                return null;
            }

            // Handle Task (fire-and-forget from client perspective)
            if (returnType == typeof(Task))
            {
                return this.client.SignalEntityAsync(this.id, operationName, input);
            }

            // Task<T> is not supported from clients as they cannot receive results
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                throw new NotSupportedException(
                    $"Method '{targetMethod.Name}' returns Task<T>, which is not supported for client-side entity proxies. " +
                    "Clients can only signal entities (fire-and-forget). Use Task (non-generic) or void return type instead. " +
                    "To get entity state, use client.Entities.GetEntityAsync().");
            }

            throw new NotSupportedException(
                $"Method '{targetMethod.Name}' has unsupported return type '{returnType.Name}'. " +
                "Client-side entity proxy methods must return void or Task.");
        }

        /// <summary>
        /// Initializes the proxy.
        /// </summary>
        /// <param name="entityClient">The durable entity client.</param>
        /// <param name="entityId">The entity instance ID.</param>
        void Initialize(DurableEntityClient entityClient, EntityInstanceId entityId)
        {
            this.client = entityClient;
            this.id = entityId;
        }
    }
}
