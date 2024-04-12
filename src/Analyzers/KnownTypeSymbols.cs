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

    public INamedTypeSymbol? FunctionOrchestrationAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.OrchestrationTriggerAttribute", ref this.functionOrchestrationAttribute);

    public INamedTypeSymbol? FunctionNameAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.FunctionAttribute", ref this.functionNameAttribute);

    public INamedTypeSymbol? TaskOrchestratorInterface => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.ITaskOrchestrator", ref this.taskOrchestratorInterface);

    public INamedTypeSymbol? TaskOrchestratorBaseClass => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.TaskOrchestrator`2", ref this.taskOrchestratorBaseClass);

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
