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
public sealed partial class KnownTypeSymbols(Compilation compilation)
{
    readonly Compilation compilation = compilation;

    INamedTypeSymbol? GetOrResolveFullyQualifiedType(string fullyQualifiedName, ref INamedTypeSymbol? field)
    {
        if (field != null)
        {
            return field;
        }

        return field = this.compilation.GetTypeByMetadataName(fullyQualifiedName);
    }
}
