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

    public class DurableFunction
    {
        public string FullTypeName { get; }
        public HashSet<string> RequiredNamespaces { get; }
        public string Name { get; }
        public DurableFunctionKind Kind { get; }
        public TypedParameter Parameter { get; }
        public string ReturnType { get; }

        public DurableFunction(
            string fullTypeName,
            string name,
            DurableFunctionKind kind,
            TypedParameter parameter,
            ITypeSymbol returnType,
            HashSet<string> requiredNamespaces)
        {
            this.FullTypeName = fullTypeName;
            this.RequiredNamespaces = requiredNamespaces;
            this.Name = name;
            this.Kind = kind;
            this.Parameter = parameter;
            this.ReturnType = SyntaxNodeUtility.GetRenderedTypeExpression(returnType, false);
        }

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
