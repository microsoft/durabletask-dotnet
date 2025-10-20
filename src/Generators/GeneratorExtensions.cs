// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators;

static class GeneratorExtensions
{
    public static bool TryGetTaskName(
        this INamedTypeSymbol symbol, out string? name)
    {
        Check.NotNull(symbol);

        name = null;
        foreach (AttributeData attributeData in symbol.GetAttributes())
        {
            if (attributeData.AttributeClass?.ToDisplayString() == TypeNames.DurableTaskAttribute)
            {
                if (attributeData.ConstructorArguments.Length == 1)
                {
                    TypedConstant arg = attributeData.ConstructorArguments[0];
                    if (arg.Value is string strValue)
                    {
                        name = strValue;
                    }
                }

                if (string.IsNullOrEmpty(name))
                {
                    name = symbol.ToDisplayString();
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the given symbol inherits from the specified base type.
    /// </summary>
    /// <param name="symbol">The class to check for inheritance.</param>
    /// <param name="baseType">The base type to check against.</param>
    /// <returns><c>true</c> if the symbol inherits from the base type; otherwise, <c>false</c>.</returns>
    public static bool InheritsFrom(this INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        Check.NotNull(symbol);
        Check.NotNull(baseType);

        INamedTypeSymbol? current = symbol;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the given symbol implements the specified interface type.
    /// </summary>
    /// <param name="symbol">The symbol to check.</param>
    /// <param name="interfaceType">The interface type to check against.</param>
    /// <returns><c>true</c> if the symbol implements the interface; otherwise, <c>false</c>.</returns>
    public static bool Implements(this INamedTypeSymbol symbol, INamedTypeSymbol interfaceType)
    {
        Check.NotNull(symbol);
        Check.NotNull(interfaceType);

        foreach (INamedTypeSymbol implementedInterface in symbol.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedInterface, interfaceType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a durable type symbol from the Microsoft.DurableTask.Abstractions assembly.
    /// </summary>
    /// <param name="compilation">The compilation to search assemblies within.</param>
    /// <param name="typeName">The name of the type to get.</param>
    /// <returns>The durable type symbol, or <c>null</c> if not found.</returns>
    public static INamedTypeSymbol? GetDurableType(this Compilation compilation, string typeName)
    {
        Check.NotNull(compilation);
        Check.NotNullOrEmpty(typeName);

        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.Name == "Microsoft.DurableTask.Abstractions")
            {
                INamedTypeSymbol? typeSymbol = assembly.GetTypeByMetadataName(typeName);
                if (typeSymbol != null)
                {
                    return typeSymbol;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the <c>ITaskOrchestrator</c> interface symbol.
    /// </summary>
    /// <param name="compilation">The compilation to search for the interface in.</param>
    /// <returns>The <c>ITaskOrchestrator</c> interface symbol.</returns>
    public static INamedTypeSymbol? GetOrchestratorInterface(this Compilation compilation)
    {
        return compilation.GetDurableType(TypeNames.TaskOrchestratorInterface);
    }

    /// <summary>
    /// Gets the <c>ITaskActivity</c> interface symbol.
    /// </summary>
    /// <param name="compilation">The compilation to search for the interface in.</param>
    /// <returns>The <c>ITaskActivity</c> interface symbol.</returns>
    public static INamedTypeSymbol? GetActivityInterface(this Compilation compilation)
    {
        return compilation.GetDurableType(TypeNames.TaskActivityInterface);
    }

    /// <summary>
    /// Gets the <c>ITaskEntity</c> interface symbol.
    /// </summary>
    /// <param name="compilation">The compilation to search for the interface in.</param>
    /// <returns>The <c>ITaskEntity</c> interface symbol.</returns>
    public static INamedTypeSymbol? GetEntityInterface(this Compilation compilation)
    {
        return compilation.GetDurableType(TypeNames.TaskEntityInterface);
    }
}
