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
        Activity,
        Entity
    }

    public class DurableFunction
    {
        public string FullTypeName { get; }
        public HashSet<string> RequiredNamespaces { get; }
        public string Name { get; }
        public DurableFunctionKind Kind { get; }
        public TypedParameter Parameter { get; }
        public string ReturnType { get; }
        public bool ReturnsVoid { get; }

        public DurableFunction(
            string fullTypeName,
            string name,
            DurableFunctionKind kind,
            TypedParameter parameter,
            ITypeSymbol returnType,
            bool returnsVoid,
            HashSet<string> requiredNamespaces)
        {
            this.FullTypeName = fullTypeName;
            this.RequiredNamespaces = requiredNamespaces;
            this.Name = name;
            this.Kind = kind;
            this.Parameter = parameter;
            this.ReturnType = SyntaxNodeUtility.GetRenderedTypeExpression(returnType, false);
            this.ReturnsVoid = returnsVoid;
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

            ITypeSymbol returnTypeSymbol = model.GetTypeInfo(returnType).Type!;
            bool returnsVoid = false;
            INamedTypeSymbol returnSymbol;

            // Check if it's a void return type
            if (returnTypeSymbol.SpecialType == SpecialType.System_Void)
            {
                returnsVoid = true;
                // For void, we'll use object as a placeholder since it won't be used
                returnSymbol = model.Compilation.GetSpecialType(SpecialType.System_Object);
            }
            // Check if it's Task (non-generic)
            else if (returnTypeSymbol is INamedTypeSymbol namedReturn && 
                     namedReturn.ContainingNamespace.ToString() == "System.Threading.Tasks" &&
                     namedReturn.Name == "Task" &&
                     namedReturn.TypeArguments.Length == 0)
            {
                returnsVoid = true;
                // For Task with no return, we'll use object as a placeholder since it won't be used
                returnSymbol = model.Compilation.GetSpecialType(SpecialType.System_Object);
            }
            // Check if it's Task<T>
            else
            {
                INamedTypeSymbol taskSymbol = model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")!;
                returnSymbol = (INamedTypeSymbol)returnTypeSymbol;
                if (SymbolEqualityComparer.Default.Equals(returnSymbol.OriginalDefinition, taskSymbol))
                {
                    // this is a Task<T> return value, lets pull out the generic.
                    returnSymbol = (INamedTypeSymbol)returnSymbol.TypeArguments[0];
                }
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

            function = new DurableFunction(fullTypeName!, name, kind, parameter, returnSymbol, returnsVoid, requiredNamespaces);
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
