﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Analyzers.Helpers;

/// <summary>
/// Provides a set of well-known types that are used by the analyzers.
/// Inspired by KnownTypeSymbols class in
/// <see href="https://github.com/dotnet/runtime/blob/2a846acb1a92e811427babe3ff3f047f98c5df02/src/libraries/System.Text.Json/gen/Helpers/KnownTypeSymbols.cs">System.Text.Json.SourceGeneration</see> source code.
/// Lazy initialization is used to avoid the the initialization of all types during class construction, since not all symbols are used by all analyzers.
/// </summary>
sealed class KnownTypeSymbols(Compilation compilation)
{
    readonly Compilation compilation = compilation;

    Cached<INamedTypeSymbol?> orchestrationTriggerAttribute;
    public INamedTypeSymbol? OrchestrationTriggerAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.OrchestrationTriggerAttribute", ref this.orchestrationTriggerAttribute);

    Cached<INamedTypeSymbol?> functionAttribute;
    public INamedTypeSymbol? FunctionAttribute => this.GetOrResolveFullyQualifiedType("Microsoft.Azure.Functions.Worker.FunctionAttribute", ref this.functionAttribute);

    INamedTypeSymbol? GetOrResolveFullyQualifiedType(string fullyQualifiedName, ref Cached<INamedTypeSymbol?> field)
    {
        if (field.HasValue)
        {
            return field.Value;
        }

        INamedTypeSymbol? type = this.compilation.GetTypeByMetadataName(fullyQualifiedName);
        field = new(type);
        return type;
    }

    // We could use Lazy<T> here, but because we need to use the `compilation` variable instance,
    // that would require us to initiate the Lazy<T> lambdas in the constructor.
    // Because not all analyzers use all symbols, we would be allocating unnecessary lambdas.
    readonly struct Cached<T>(T value)
    {
        public readonly bool HasValue = true;
        public readonly T Value = value;
    }
}
