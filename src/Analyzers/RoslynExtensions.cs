// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers;

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

    public static bool ImplementsInterface(this INamedTypeSymbol symbol, ISymbol interfaceSymbol)
    {
        return symbol.AllInterfaces.Any(i => interfaceSymbol.Equals(i, SymbolEqualityComparer.Default));
    }

    public static bool BaseTypeIsConstructedFrom(this INamedTypeSymbol symbol, ITypeSymbol type)
    {
        INamedTypeSymbol? baseType = symbol.BaseType;
        while (baseType != null)
        {
            if (baseType.ConstructedFrom.Equals(type, SymbolEqualityComparer.Default))
            {
                return true;
            }
            baseType = baseType.BaseType;
        }
        return false;
    }

    public static IMethodSymbol? GetOverridenMethod(this INamedTypeSymbol typeSymbol, IMethodSymbol methodSymbol)
    {
        IEnumerable<IMethodSymbol> methods = typeSymbol.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>();
        return methods.FirstOrDefault(m => m.OverriddenMethod != null && m.OverriddenMethod.OriginalDefinition.Equals(methodSymbol, SymbolEqualityComparer.Default));
    }

    public static IEnumerable<MethodDeclarationSyntax> GetSyntaxNodes(this IMethodSymbol methodSymbol)
    {
        return methodSymbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>();
    }

    public static Optional<object?> GetConstantValueFromAttribute(this IArgumentOperation argumentOperation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        LiteralExpressionSyntax? nameLiteralSyntax = argumentOperation.Syntax.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault();

        return semanticModel.GetConstantValue(nameLiteralSyntax, cancellationToken);
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
