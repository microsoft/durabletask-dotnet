// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Analyzers;

/// <summary>
/// Provides a set of well-known types that are used by the analyzers.
/// Inspired by KnownTypeSymbols class in
/// <see href="https://github.com/dotnet/runtime/blob/2a846acb1a92e811427babe3ff3f047f98c5df02/src/libraries/System.Text.Json/gen/Helpers/KnownTypeSymbols.cs">System.Text.Json.SourceGeneration</see> source code.
/// Lazy initialization is used to avoid the the initialization of all types during class construction, since not all symbols are used by all analyzers.
/// </summary>
sealed class KnownTypeSymbols(Compilation compilation)
{
    readonly Compilation compilation = compilation;

    INamedTypeSymbol? functionOrchestrationAttribute;
    INamedTypeSymbol? functionNameAttribute;
    INamedTypeSymbol? taskOrchestratorInterface;
    INamedTypeSymbol? taskOrchestratorBaseClass;
    INamedTypeSymbol? durableTaskRegistry;

    /// <summary>
    /// Gets an OrchestrationTriggerAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? FunctionOrchestrationAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.OrchestrationTriggerAttribute", ref this.functionOrchestrationAttribute);

    /// <summary>
    /// Gets a FunctionNameAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? FunctionNameAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.FunctionAttribute", ref this.functionNameAttribute);

    /// <summary>
    /// Gets an ITaskOrchestrator type symbol.
    /// </summary>
    public INamedTypeSymbol? TaskOrchestratorInterface => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.ITaskOrchestrator", ref this.taskOrchestratorInterface);

    /// <summary>
    /// Gets a TaskOrchestrator type symbol.
    /// </summary>
    public INamedTypeSymbol? TaskOrchestratorBaseClass => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.TaskOrchestrator`2", ref this.taskOrchestratorBaseClass);

    /// <summary>
    /// Gets a DurableTaskRegistry type symbol.
    /// </summary>
    public INamedTypeSymbol? DurableTaskRegistry => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.DurableTaskRegistry", ref this.durableTaskRegistry);

    INamedTypeSymbol? GetOrResolveFullyQualifiedType(string fullyQualifiedName, ref INamedTypeSymbol? field)
    {
        if (field != null)
        {
            return field;
        }

        return field = this.compilation.GetTypeByMetadataName(fullyQualifiedName);
    }
}
