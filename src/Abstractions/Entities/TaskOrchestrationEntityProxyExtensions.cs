// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;

namespace Microsoft.DurableTask.Entities;

/// <summary>
/// Extension methods for creating strongly-typed entity proxies.
/// </summary>
public static class TaskOrchestrationEntityProxyExtensions
{
    /// <summary>
    /// Creates a strongly-typed proxy for invoking entity operations.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type. Must extend <see cref="IEntityProxy"/>.</typeparam>
    /// <param name="feature">The entity feature.</param>
    /// <param name="id">The entity instance ID.</param>
    /// <returns>A strongly-typed proxy for the entity.</returns>
    /// <remarks>
    /// <para>
    /// The proxy interface should define methods that correspond to entity operations.
    /// Each method invocation will be translated to a call or signal to the entity, depending on the return type:
    /// </para>
    /// <list type="bullet">
    /// <item>Methods returning <see cref="Task"/> or <see cref="Task{TResult}"/> will use CallEntityAsync.</item>
    /// <item>Methods returning void will use SignalEntityAsync (fire-and-forget).</item>
    /// </list>
    /// <para>
    /// Example:
    /// <code>
    /// public interface ICounter : IEntityProxy
    /// {
    ///     Task&lt;int&gt; Add(int value);
    ///     Task&lt;int&gt; Get();
    ///     void Reset();
    /// }
    ///
    /// var counter = context.Entities.CreateProxy&lt;ICounter&gt;(new EntityInstanceId("Counter", "myCounter"));
    /// int result = await counter.Add(5);
    /// </code>
    /// </para>
    /// </remarks>
    public static TEntityProxy CreateProxy<TEntityProxy>(
        this TaskOrchestrationEntityFeature feature,
        EntityInstanceId id)
        where TEntityProxy : class, IEntityProxy
    {
        Check.NotNull(feature);
        return EntityProxy<TEntityProxy>.Create(feature, id);
    }

    /// <summary>
    /// Creates a strongly-typed proxy for invoking entity operations.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type. Must extend <see cref="IEntityProxy"/>.</typeparam>
    /// <param name="feature">The entity feature.</param>
    /// <param name="entityName">The entity name.</param>
    /// <param name="entityKey">The entity key.</param>
    /// <returns>A strongly-typed proxy for the entity.</returns>
    public static TEntityProxy CreateProxy<TEntityProxy>(
        this TaskOrchestrationEntityFeature feature,
        string entityName,
        string entityKey)
        where TEntityProxy : class, IEntityProxy
    {
        return CreateProxy<TEntityProxy>(feature, new EntityInstanceId(entityName, entityKey));
    }

    /// <summary>
    /// Proxy implementation for entity invocation.
    /// </summary>
    /// <typeparam name="TEntityProxy">The entity proxy interface type.</typeparam>
    class EntityProxy<TEntityProxy> : DispatchProxy
        where TEntityProxy : class, IEntityProxy
    {
        TaskOrchestrationEntityFeature feature = null!;
        EntityInstanceId id;

        /// <summary>
        /// Creates a proxy instance.
        /// </summary>
        /// <param name="entityFeature">The entity feature.</param>
        /// <param name="entityId">The entity instance ID.</param>
        /// <returns>The proxy instance.</returns>
        public static TEntityProxy Create(TaskOrchestrationEntityFeature entityFeature, EntityInstanceId entityId)
        {
            object proxy = Create<TEntityProxy, EntityProxy<TEntityProxy>>();
            ((EntityProxy<TEntityProxy>)proxy).Initialize(entityFeature, entityId);
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

            // Handle void methods - these are fire-and-forget signals
            if (returnType == typeof(void))
            {
                // Fire and forget - we can't await this in a sync method, so we need to return immediately
                // This will schedule the signal but not wait for it
                Task signalTask = this.feature.SignalEntityAsync(this.id, operationName, input);

                // For void methods, we complete synchronously but the signal is scheduled
                // This matches the behavior of SignalEntityAsync which returns a Task
                // that completes when the signal is scheduled, not when it's processed
                signalTask.ConfigureAwait(false).GetAwaiter().GetResult();
                return null;
            }

            // Handle Task (non-generic) - call without expecting a result
            if (returnType == typeof(Task))
            {
                return this.feature.CallEntityAsync(this.id, operationName, input);
            }

            // Handle Task<TResult> - call with a result
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                Type resultType = returnType.GetGenericArguments()[0];
                MethodInfo? callMethod = typeof(TaskOrchestrationEntityFeature)
                    .GetMethods()
                    .Where(m => m.Name == nameof(TaskOrchestrationEntityFeature.CallEntityAsync) &&
                                m.IsGenericMethod &&
                                m.GetGenericArguments().Length == 1)
                    .Select(m => m.MakeGenericMethod(resultType))
                    .FirstOrDefault(m =>
                    {
                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 4 &&
                               parameters[0].ParameterType == typeof(EntityInstanceId) &&
                               parameters[1].ParameterType == typeof(string) &&
                               parameters[2].ParameterType == typeof(object) &&
                               parameters[3].ParameterType == typeof(CallEntityOptions);
                    });

                if (callMethod is null)
                {
                    throw new InvalidOperationException($"Could not find CallEntityAsync method for return type {returnType}");
                }

                return callMethod.Invoke(this.feature, new object?[] { this.id, operationName, input, null });
            }

            throw new NotSupportedException(
                $"Method '{targetMethod.Name}' has unsupported return type '{returnType.Name}'. " +
                "Entity proxy methods must return void, Task, or Task<T>.");
        }

        /// <summary>
        /// Initializes the proxy.
        /// </summary>
        /// <param name="entityFeature">The entity feature.</param>
        /// <param name="entityId">The entity instance ID.</param>
        void Initialize(TaskOrchestrationEntityFeature entityFeature, EntityInstanceId entityId)
        {
            this.feature = entityFeature;
            this.id = entityId;
        }
    }
}
