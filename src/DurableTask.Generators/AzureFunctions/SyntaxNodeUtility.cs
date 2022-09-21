// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DurableTask.Generators.AzureFunctions
{
    public static class SyntaxNodeUtility
    {
        public static bool TryGetFunctionName(SemanticModel model, MethodDeclarationSyntax method, out string? functionName)
        {
            functionName = null;
            if (TryGetAttributeByName(method, "Function", out AttributeSyntax? functionNameAttribute) && functionNameAttribute != null)
            {
                if (functionNameAttribute.ArgumentList?.Arguments.Count == 1)
                {
                    ExpressionSyntax expression = functionNameAttribute.ArgumentList.Arguments.First().Expression;
                    Optional<object?> constant = model.GetConstantValue(expression);
                    if (!constant.HasValue)
                    {
                        return false;
                    }

                    functionName = constant.ToString();
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetReturnType(MethodDeclarationSyntax method, out TypeSyntax returnTypeSyntax)
        {
            returnTypeSyntax = method.ReturnType;
            return true;
        }

        public static bool TryGetFunctionKind(MethodDeclarationSyntax method, out DurableFunctionKind kind)
        {
            var parameters = method.ParameterList.Parameters;

            foreach (var parameterSyntax in parameters)
            {
                var parameterAttributes = parameterSyntax.AttributeLists.SelectMany(a => a.Attributes);

                foreach (var attribute in parameterAttributes)
                {
                    if (attribute.ToString().Equals("OrchestrationTrigger"))
                    {
                        kind = DurableFunctionKind.Orchestration;
                        return true;
                    }

                    if (attribute.ToString().Equals("ActivityTrigger"))
                    {
                        kind = DurableFunctionKind.Activity;
                        return true;
                    }
                }
            }

            kind = DurableFunctionKind.Unknown;
            return false;
        }

        public static bool TryGetRequiredNamespaces(SemanticModel model, List<TypeSyntax> types, out HashSet<string>? requiredNamespaces)
        {
            requiredNamespaces = new HashSet<string>();

            var remaining = new Queue<TypeSyntax>(types);

            while (remaining.Any())
            {
                var toProcess = remaining.Dequeue();

                if (toProcess is PredefinedTypeSyntax)
                    continue;

                TypeInfo typeInfo = model.GetTypeInfo(toProcess);
                if (typeInfo.Type == null)
                {
                    return false;
                }

                if (toProcess is not PredefinedTypeSyntax && typeInfo.Type.ContainingNamespace.IsGlobalNamespace)
                {
                    requiredNamespaces = null;
                    return false;
                }

                requiredNamespaces.Add(typeInfo.Type!.ContainingNamespace.ToDisplayString());

                if (toProcess is GenericNameSyntax genericType)
                {
                    foreach (var typeArgument in genericType.TypeArgumentList.Arguments)
                    {
                        remaining.Enqueue(typeArgument);
                    }
                }
            }

            return true;
        }

        internal static bool TryGetParameter(
            MethodDeclarationSyntax method,
            DurableFunctionKind kind,
            out TypedParameter? parameter)
        {
            foreach (var methodParam in method.ParameterList.Parameters)
            {
                if (methodParam.Type == null)
                {
                    continue;
                }

                foreach (AttributeListSyntax list in methodParam.AttributeLists)
                {
                    foreach (AttributeSyntax attribute in list.Attributes)
                    {
                        string attributeName = attribute.Name.ToString();
                        if ((kind == DurableFunctionKind.Activity && attributeName == "ActivityTrigger") ||
                            (kind == DurableFunctionKind.Orchestration && attributeName == "OrchestratorTrigger"))
                        {
                            parameter = new TypedParameter(methodParam.Type, methodParam.Identifier.ToString());
                            return true;
                        }
                    }
                }
            }

            parameter = null;
            return false;
        }

        public static bool TryGetQualifiedTypeName(SemanticModel model, MethodDeclarationSyntax method, out string? fullTypeName)
        {
            var symbol = model.GetEnclosingSymbol(method.SpanStart);
            if (symbol == null)
            {
                fullTypeName = null;
                return false;
            }

            fullTypeName = $@"{symbol.ToDisplayString()}.{method.Identifier}";
            return true;
        }

        static bool TryGetAttributeByName(MethodDeclarationSyntax method, string attributeName, out AttributeSyntax? attribute)
        {
            attribute = method.AttributeLists.SelectMany(a => a.Attributes).FirstOrDefault(a => a.Name.NormalizeWhitespace().ToFullString().Equals(attributeName));
            return attribute != null;
        }
    }
}
