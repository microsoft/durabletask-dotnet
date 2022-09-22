// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
        public TypeSyntax ReturnType { get; }

        public DurableFunction(string fullTypeName, string name, DurableFunctionKind kind, TypedParameter parameter, TypeSyntax returnTypeSyntax, HashSet<string> requiredNamespaces)
        {
            this.FullTypeName = fullTypeName;
            this.RequiredNamespaces = requiredNamespaces;
            this.Name = name;
            this.Kind = kind;
            this.Parameter = parameter;
            this.ReturnType = returnTypeSyntax;
        }

        public static bool TryParse(SemanticModel model, MethodDeclarationSyntax method, out DurableFunction? function)
        {
            function = null;

            if (!SyntaxNodeUtility.TryGetFunctionName(model, method, out string? name) || name == null)
            {
                return false;
            }

            if (!SyntaxNodeUtility.TryGetFunctionKind(method, out DurableFunctionKind kind))
            {
                return false;
            }

            if (!SyntaxNodeUtility.TryGetReturnType(method, out TypeSyntax returnType))
            {
                return false;
            }

            INamedTypeSymbol taskSymbol = model.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")!;
            INamedTypeSymbol returnSymbol = (INamedTypeSymbol)model.GetTypeInfo(returnType).Type!;
            if (SymbolEqualityComparer.Default.Equals(returnSymbol.OriginalDefinition, taskSymbol))
            {
                // this is a Task<T> return value, lets pull out the generic.
                returnType = ((GenericNameSyntax)returnType).TypeArgumentList.Arguments[0];
            }

            if (!SyntaxNodeUtility.TryGetParameter(model, method, kind, out TypedParameter? parameter) || parameter == null)
            {
                return false;
            }

            if (!SyntaxNodeUtility.TryGetQualifiedTypeName(model, method, out string? fullTypeName))
            {
                return false;
            }

            List<TypeSyntax>? usedTypes = new();
            usedTypes.Add(returnType);
            usedTypes.Add(parameter.Type);

            if (!SyntaxNodeUtility.TryGetRequiredNamespaces(model, usedTypes, out HashSet<string>? requiredNamespaces))
            {
                return false;
            }

            requiredNamespaces!.UnionWith(GetRequiredGlobalNamespaces());

            function = new DurableFunction(fullTypeName!, name, kind, parameter, returnType, requiredNamespaces);
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
