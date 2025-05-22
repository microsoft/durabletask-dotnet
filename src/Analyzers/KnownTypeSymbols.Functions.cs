// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Dapr.DurableTask.Analyzers;

/// <summary>
/// Provides a set of well-known types that are used by the analyzers.
/// Inspired by KnownTypeSymbols class in
/// <see href="https://github.com/dotnet/runtime/blob/2a846acb1a92e811427babe3ff3f047f98c5df02/src/libraries/System.Text.Json/gen/Helpers/KnownTypeSymbols.cs">System.Text.Json.SourceGeneration</see> source code.
/// Lazy initialization is used to avoid the the initialization of all types during class construction, since not all symbols are used by all analyzers.
/// </summary>
public sealed partial class KnownTypeSymbols
{
    INamedTypeSymbol? functionOrchestrationAttribute;
    INamedTypeSymbol? functionNameAttribute;
    INamedTypeSymbol? durableClientAttribute;
    INamedTypeSymbol? activityTriggerAttribute;
    INamedTypeSymbol? entityTriggerAttribute;
    INamedTypeSymbol? taskEntityDispatcher;

    /// <summary>
    /// Gets an OrchestrationTriggerAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? FunctionOrchestrationAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.OrchestrationTriggerAttribute", ref this.functionOrchestrationAttribute);

    /// <summary>
    /// Gets a FunctionNameAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? FunctionNameAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.FunctionAttribute", ref this.functionNameAttribute);

    /// <summary>
    /// Gets a DurableClientAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? DurableClientAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.DurableClientAttribute", ref this.durableClientAttribute);

    /// <summary>
    /// Gets an ActivityTriggerAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? ActivityTriggerAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.ActivityTriggerAttribute", ref this.activityTriggerAttribute);

    /// <summary>
    /// Gets an EntityTriggerAttribute type symbol.
    /// </summary>
    public INamedTypeSymbol? EntityTriggerAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.EntityTriggerAttribute", ref this.entityTriggerAttribute);

    /// <summary>
    /// Gets a TaskEntityDispatcher type symbol.
    /// </summary>
    public INamedTypeSymbol? TaskEntityDispatcher => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.TaskEntityDispatcher", ref this.taskEntityDispatcher);
}
