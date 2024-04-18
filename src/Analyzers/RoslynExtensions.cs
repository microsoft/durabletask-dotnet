// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers;

/// <summary>
/// Extension methods for working with Roslyn types.
/// </summary>
static class RoslynExtensions
{
    /// <summary>
    /// Tries to get the value of an attribute that has a single value.
    /// </summary>
    /// <typeparam name="T">Convertion Type.</typeparam>
    /// <param name="symbol">Symbol containing the annotation.</param>
    /// <param name="attributeSymbol">Attribute to look for.</param>
    /// <param name="value">Retrieved value from the attribute instance.</param>
    /// <returns>true if the method succeeded to retrieve the value, false otherwise.</returns>
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

    /// <summary>
    /// Determines whether a method has a parameter with the specified attribute.
    /// </summary>
    /// <param name="methodSymbol">Method symbol.</param>
    /// <param name="attributeSymbol">Attribute class symbol.</param>
    /// <returns>True if the method has the parameter, false otherwise.</returns>
    public static bool ContainsAttributeInAnyMethodArguments(this IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
    {
        return methodSymbol.Parameters
            .SelectMany(p => p.GetAttributes())
            .Any(a => attributeSymbol.Equals(a.AttributeClass, SymbolEqualityComparer.Default));
    }

    /// <summary>
    /// Determines whether the base type of a symbol is constructed from a specified type.
    /// </summary>
    /// <param name="symbol">Constructed Type Symbol.</param>
    /// <param name="type">Contructed From Type Symbol.</param>
    /// <returns>True if the base type is constructed from the specified type, false otherwise.</returns>
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

    /// <summary>
    /// Gets the method that overrides a type's method.
    /// </summary>
    /// <param name="typeSymbol">Type symbol containing the methods to look for.</param>
    /// <param name="methodSymbol">Method to look for in the type symbol.</param>
    /// <returns>The overriden method.</returns>
    public static IMethodSymbol? GetOverridenMethod(this INamedTypeSymbol typeSymbol, IMethodSymbol methodSymbol)
    {
        IEnumerable<IMethodSymbol> methods = typeSymbol.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>();
        return methods.FirstOrDefault(m => m.OverriddenMethod != null && m.OverriddenMethod.OriginalDefinition.Equals(methodSymbol, SymbolEqualityComparer.Default));
    }

    /// <summary>
    /// Gets the syntax nodes of a method symbol.
    /// </summary>
    /// <param name="methodSymbol">Method symbol.</param>
    /// <returns>The collection of syntax nodes of a given method symbol.</returns>
    public static IEnumerable<MethodDeclarationSyntax> GetSyntaxNodes(this IMethodSymbol methodSymbol)
    {
        return methodSymbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<MethodDeclarationSyntax>();
    }

    /// <summary>
    /// Gets the literal value of an argument operation.
    /// </summary>
    /// <param name="argumentOperation">Argument operation.</param>
    /// <param name="semanticModel">Semantical model.</param>
    /// <param name="cancellationToken">Cancellation Token.</param>
    /// <returns>The literal value of the argument.</returns>
    public static Optional<object?> GetConstantValueFromAttribute(this IArgumentOperation argumentOperation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        LiteralExpressionSyntax? nameLiteralSyntax = argumentOperation.Syntax.DescendantNodes().OfType<LiteralExpressionSyntax>().FirstOrDefault();

        return semanticModel.GetConstantValue(nameLiteralSyntax, cancellationToken);
    }

    /// <summary>
    /// Reports a diagnostic for a given operation.
    /// </summary>
    /// <param name="ctx">Context for a compilation action.</param>
    /// <param name="descriptor">Diagnostic Descriptor to be reported.</param>
    /// <param name="operation">Operation which the location will be extracted.</param>
    /// <param name="messageArgs">Diagnostic message arguments to be reported.</param>
    public static void ReportDiagnostic(this CompilationAnalysisContext ctx, DiagnosticDescriptor descriptor, IOperation operation, params string[] messageArgs)
    {
        ctx.ReportDiagnostic(BuildDiagnostic(descriptor, operation.Syntax, messageArgs));
    }

    static Diagnostic BuildDiagnostic(DiagnosticDescriptor descriptor, SyntaxNode syntaxNode, params string[] messageArgs)
    {
        return Diagnostic.Create(descriptor, syntaxNode.GetLocation(), messageArgs);
    }

    static bool TryGetConstructorArgumentsFromAttribute(this ISymbol? symbol, INamedTypeSymbol attributeSymbol, out ImmutableArray<TypedConstant> constructorArguments)
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
}
