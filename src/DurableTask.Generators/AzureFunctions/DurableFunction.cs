//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DurableTask.Generators.AzureFunctions
{
    public enum DurableFunctionKind
    {
        Unknonwn,
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

            if (!SyntaxNodeUtility.TryGetParameter(method, kind, out TypedParameter? parameter) || parameter == null)
            {
                return false;
            }

            if (!SyntaxNodeUtility.TryGetQualifiedTypeName(model, method, out string fullTypeName))
            {
                return false;
            }

            List<TypeSyntax>? usedTypes = new();
            usedTypes.Add(returnType);
            usedTypes.Add(parameter.Type);

            if (!SyntaxNodeUtility.TryGetRequiredNamespaces(model, usedTypes, out HashSet<string> requiredNamespaces))
            {
                return false;
            }

            requiredNamespaces.UnionWith(GetRequiredGlobalNamespaces());

            function = new DurableFunction(fullTypeName, name, kind, parameter, returnType, requiredNamespaces);
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
