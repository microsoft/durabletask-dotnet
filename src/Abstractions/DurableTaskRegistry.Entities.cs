// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask;

/// <summary>
/// Container for registered <see cref="ITaskOrchestrator" />, <see cref="ITaskActivity" />,
/// and <see cref="ITaskEntity"/> implementations.
/// </summary>
public partial class DurableTaskRegistry
{
    /// <summary>
    /// Registers an entity factory.
    /// </summary>
    /// <param name="name">The name of the entity to register.</param>
    /// <param name="type">The entity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity(TaskName name, Type type)
    {
        // TODO: Compile a constructor expression for performance.
        Check.ConcreteType<ITaskEntity>(type);
        return this.AddEntity(name, sp => (ITaskEntity)ActivatorUtilities.CreateInstance(sp, type));
    }

    /// <summary>
    /// Registers an entity factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <param name="type">The entity type.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity(Type type)
        => this.AddEntity(type.GetTaskName(), type);

    /// <summary>
    /// Registers an entity factory.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to register.</typeparam>
    /// <param name="name">The name of the entity to register.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity<TEntity>(TaskName name)
        where TEntity : class, ITaskEntity
        => this.AddEntity(name, typeof(TEntity));

    /// <summary>
    /// Registers an entity factory. The TaskName used is derived from the provided type information.
    /// </summary>
    /// <typeparam name="TEntity">The type of entity to register.</typeparam>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity<TEntity>()
        where TEntity : class, ITaskEntity
        => this.AddEntity(typeof(TEntity));

    /// <summary>
    /// Registers an entity singleton.
    /// </summary>
    /// <param name="name">The name of the entity to register.</param>
    /// <param name="entity">The entity instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity(TaskName name, ITaskEntity entity)
    {
        Check.NotNull(entity);
        return this.AddEntity(name, _ => entity);
    }

    /// <summary>
    /// Registers an entity singleton.
    /// </summary>
    /// <param name="entity">The entity instance to use.</param>
    /// <returns>The same registry, for call chaining.</returns>
    public DurableTaskRegistry AddEntity(ITaskEntity entity)
    {
        Check.NotNull(entity);
        return this.AddEntity(entity.GetType().GetTaskName(), entity);
    }
}
