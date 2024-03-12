// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DurableTask.Generators.AzureFunctions
{
    public enum DurableFunctionKind
    {
        Unknown,
        Orchestration,
        Activity
    }

    public class DurableFunction(
        string fullTypeName,
        string name,
        DurableFunctionKind kind,
        TypedParameter parameter,
        ITypeSymbol returnType,
        HashSet<string> requiredNamespaces)
    {
        public string FullTypeName { get; } = fullTypeName;

        public HashSet<string> RequiredNamespaces { get; } = requiredNamespaces;

        public string Name { get; } = name;

        public DurableFunctionKind Kind { get; } = kind;

        public TypedParameter Parameter { get; } = parameter;

        public string ReturnType { get; } = SyntaxNodeUtility.GetRenderedTypeExpression(returnType, false);

        public static bool TryParse(SemanticModel model, MethodDeclarationSyntax method, out DurableFunction? function)
        {
            if (!SyntaxNodeUtility.TryGetFunctionName(model, method, out string? name) || name == null)
            {
                function = null;
                return false;
            }

            if (!SyntaxNodeUtility.TryGetFunctionKind(method, out DurableFunctionKind kind))
            {
                function = null;
                return false;
            }

            if (!SyntaxNodeUtility.TryGetReturnType(method, out TypeSyntax returnType))
            {
                function = null;
                return false;
            }

            INamedTypeSymbol taskSymbol = model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")!;
            INamedTypeSymbol returnSymbol = (INamedTypeSymbol)model.GetTypeInfo(returnType).Type!;
            if (SymbolEqualityComparer.Default.Equals(returnSymbol.OriginalDefinition, taskSymbol))
            {
                // this is a Task<T> return value, lets pull out the generic.
                returnSymbol = (INamedTypeSymbol)returnSymbol.TypeArguments[0];
            }

            if (!SyntaxNodeUtility.TryGetParameter(model, method, kind, out TypedParameter? parameter) || parameter == null)
            {
                function = null;
                return false;
            }

            if (!SyntaxNodeUtility.TryGetQualifiedTypeName(model, method, out string? fullTypeName))
            {
                function = null;
                return false;
            }

            List<INamedTypeSymbol> usedTypes = new()
            {
                returnSymbol,
                parameter.Type
            };

            if (!SyntaxNodeUtility.TryGetRequiredNamespaces(usedTypes, out HashSet<string>? requiredNamespaces))
            {
                function = null;
                return false;
            }

            requiredNamespaces!.UnionWith(GetRequiredGlobalNamespaces());

            function = new DurableFunction(fullTypeName!, name, kind, parameter, returnSymbol, requiredNamespaces);
            return true;
        }

        static string[] GetRequiredGlobalNamespaces()
        {
            return new[]
            {
                "System.Threading.Tasks",
                "DurableTask"
            };
        }
    }
}
