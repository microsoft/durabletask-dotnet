// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Analyzers.Helpers;

static class RoslynExtensions
{
    public static bool TryGetSingleValueFromAttribute<T>(this ISymbol? symbol, INamedTypeSymbol attributeSymbol, out T value)
    {
        if (symbol.TryGetConstructorArgumentsFromAttribute(attributeSymbol, out ImmutableArray<TypedConstant> constructorArguments))
        {
            object? valueObj = constructorArguments.FirstOrDefault().Value;
            if (valueObj != null)
            {
                value = (T)valueObj;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public static bool TryGetConstructorArgumentsFromAttribute(this ISymbol? symbol, INamedTypeSymbol attributeSymbol, out ImmutableArray<TypedConstant> constructorArguments)
    {
        if (symbol != null)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attributeSymbol.Equals(attribute.AttributeClass, SymbolEqualityComparer.Default))
                {
                    constructorArguments = attribute.ConstructorArguments;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool ContainsAttributeInAnyMethodArguments(this IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
    {
        return methodSymbol.Parameters
            .SelectMany(p => p.GetAttributes())
            .Any(a => attributeSymbol.Equals(a.AttributeClass, SymbolEqualityComparer.Default));
    }

    public static void ReportDiagnostic(this CompilationAnalysisContext ctx, DiagnosticDescriptor descriptor, IOperation operation, params string[] messageArgs)
    {
        ctx.ReportDiagnostic(BuildDiagnostic(descriptor, operation.Syntax, messageArgs));
    }

    public static Diagnostic BuildDiagnostic(DiagnosticDescriptor descriptor, SyntaxNode syntaxNode, params string[] messageArgs)
    {
        return Diagnostic.Create(descriptor, syntaxNode.GetLocation(), messageArgs);
    }
}
