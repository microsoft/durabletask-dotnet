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
            ITypeSymbol? returnType,
            bool returnsVoid,
            HashSet<string> requiredNamespaces)
        {
            this.FullTypeName = fullTypeName;
            this.RequiredNamespaces = requiredNamespaces;
            this.Name = name;
            this.Kind = kind;
            this.Parameter = parameter;
            this.ReturnType = returnType != null ? SyntaxNodeUtility.GetRenderedTypeExpression(returnType, false) : string.Empty;
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

            ITypeSymbol? returnTypeSymbol = model.GetTypeInfo(returnType).Type;
            if (returnTypeSymbol == null || returnTypeSymbol.TypeKind == TypeKind.Error)
            {
                function = null;
                return false;
            }

            bool returnsVoid = false;
            INamedTypeSymbol? returnSymbol = null;

            // Check if it's a void return type
            if (returnTypeSymbol.SpecialType == SpecialType.System_Void)
            {
                returnsVoid = true;
                // returnSymbol is left as null since void has no type to track
            }
            // Check if it's Task (non-generic)
            else if (returnTypeSymbol is INamedTypeSymbol namedReturn)
            {
                INamedTypeSymbol? nonGenericTaskSymbol = model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
                if (nonGenericTaskSymbol != null && SymbolEqualityComparer.Default.Equals(namedReturn, nonGenericTaskSymbol))
                {
                    returnsVoid = true;
                    // returnSymbol is left as null since Task (non-generic) has no return type to track
                }
                // Check if it's Task<T>
                else
                {
                    INamedTypeSymbol? taskSymbol = model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
                    returnSymbol = namedReturn;
                    if (taskSymbol != null && SymbolEqualityComparer.Default.Equals(returnSymbol.OriginalDefinition, taskSymbol))
                    {
                        // this is a Task<T> return value, lets pull out the generic.
                        ITypeSymbol typeArg = returnSymbol.TypeArguments[0];
                        if (typeArg is not INamedTypeSymbol namedTypeArg)
                        {
                            function = null;
                            return false;
                        }
                        returnSymbol = namedTypeArg;
                    }
                }
            }
            else
            {
                // returnTypeSymbol is not INamedTypeSymbol, which is unexpected
                function = null;
                return false;
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

            // Build list of types used for namespace resolution
            List<INamedTypeSymbol> usedTypes = new()
            {
                parameter.Type
            };

            // Only include return type if it's not void
            if (returnSymbol != null)
            {
                usedTypes.Add(returnSymbol);
            }

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
