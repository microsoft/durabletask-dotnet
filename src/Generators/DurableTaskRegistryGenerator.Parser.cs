// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators;

public partial class DurableTaskRegistryGenerator
{
    enum DurableTaskType
    {
        Unknown,
        Orchestrator,
        Activity,
        Entity,
    }

    /// <summary>
    /// Collects DurableTask information from a class with the DurableTaskAttribute.
    /// </summary>
    /// <param name="context">The syntax context.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A <see cref="DurableTaskDetails"/> object if the type is a DurableTask, otherwise null.</returns>
    /// <remarks>
    /// It is important the return type implements <see cref="IEquatable{T}"/> for the incremental generator to
    /// properly cache results. Record types satisfy this requirement.
    /// </remarks>
    static DurableTaskDetails? GetTaskDetails(
        GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        if (context.TargetSymbol is not INamedTypeSymbol namedTypeSymbol)
        {
            return null;
        }

        if (namedTypeSymbol.IsAbstract)
        {
            // TODO: Report diagnostic - abstract classes cannot use this attribute.
            return null;
        }

        cancellation.ThrowIfCancellationRequested();

        DurableInterfaces interfaces = DurableInterfaces.Create(context.SemanticModel.Compilation);
        if (interfaces == default)
        {
            // TODO: Report diagnostic - unable to find required interfaces.
            return null;
        }

        if (namedTypeSymbol.TryGetTaskName(out string? name))
        {
            DurableTaskType type = DurableTaskType.Unknown;
            if (namedTypeSymbol.Implements(interfaces.OrchestratorInterface))
            {
                type = DurableTaskType.Orchestrator;
            }
            else if (namedTypeSymbol.Implements(interfaces.ActivityInterface))
            {
                type = DurableTaskType.Activity;
            }
            else if (namedTypeSymbol.Implements(interfaces.EntityInterface))
            {
                type = DurableTaskType.Entity;
            }

            if (type == DurableTaskType.Unknown)
            {
                // TODO: Report diagnostic - unknown durable task type.
            }
            else
            {
                return new DurableTaskDetails(type, name!, namedTypeSymbol.ToDisplayString());
            }
        }

        return null;
    }

    record DurableTaskDetails(DurableTaskType Type, string TaskName, string ClassName)
    {
        public string RegisterMethod
        {
            get
            {
                return this.Type switch
                {
                    DurableTaskType.Orchestrator => "AddOrchestrator",
                    DurableTaskType.Activity => "AddActivity",
                    DurableTaskType.Entity => "AddEntity",
                    _ => throw new InvalidOperationException("Unknown durable task type."),
                };
            }
        }
    }
}
