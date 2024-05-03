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
public sealed partial class KnownTypeSymbols
{
    INamedTypeSymbol? taskOrchestratorInterface;
    INamedTypeSymbol? taskOrchestratorBaseClass;
    INamedTypeSymbol? durableTaskRegistry;
    INamedTypeSymbol? taskOrchestrationContext;
    INamedTypeSymbol? durableTaskClient;

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

    /// <summary>
    /// Gets a TaskOrchestrationContext type symbol.
    /// </summary>
    public INamedTypeSymbol? TaskOrchestrationContext => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.TaskOrchestrationContext", ref this.taskOrchestrationContext);


    /// <summary>
    /// Gets a DurableTaskClient type symbol.
    /// </summary>
    public INamedTypeSymbol? DurableTaskClient => this.GetOrResolveFullyQualifiedType("Microsoft.DurableTask.Client.DurableTaskClient", ref this.durableTaskClient);
}
