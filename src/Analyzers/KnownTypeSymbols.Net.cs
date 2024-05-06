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
    INamedTypeSymbol? guid;
    INamedTypeSymbol? thread;
    INamedTypeSymbol? task;
    INamedTypeSymbol? taskT;
    INamedTypeSymbol? cancellationToken;

    /// <summary>
    /// Gets a Guid type symbol.
    /// </summary>
    public INamedTypeSymbol? GuidType => this.GetOrResolveFullyQualifiedType(typeof(Guid).FullName, ref this.guid);

    /// <summary>
    /// Gets a Thread type symbol.
    /// </summary>
    public INamedTypeSymbol? Thread => this.GetOrResolveFullyQualifiedType(typeof(Thread).FullName, ref this.thread);

    /// <summary>
    /// Gets a Task type symbol.
    /// </summary>
    public INamedTypeSymbol? Task => this.GetOrResolveFullyQualifiedType(typeof(Task).FullName, ref this.task);

    /// <summary>
    /// Gets a Task&lt;T&gt; type symbol.
    /// </summary>
    public INamedTypeSymbol? TaskT => this.GetOrResolveFullyQualifiedType(typeof(Task<>).FullName, ref this.taskT);

    /// <summary>
    /// Gets a CancellationToken type symbol.
    /// </summary>
    public INamedTypeSymbol? CancellationToken => this.GetOrResolveFullyQualifiedType(typeof(CancellationToken).FullName, ref this.cancellationToken);
}
