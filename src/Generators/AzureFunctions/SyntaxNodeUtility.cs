// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dapr.DurableTask.Generators.AzureFunctions
{
    static class SyntaxNodeUtility
    {
        public static bool TryGetFunctionName(
            SemanticModel model, MethodDeclarationSyntax method, out string? functionName)
        {
            if (TryGetAttributeByName(
                method, "Function", out AttributeSyntax? functionNameAttribute) && functionNameAttribute != null)
            {
                if (functionNameAttribute.ArgumentList?.Arguments.Count == 1)
                {
                    ExpressionSyntax expression = functionNameAttribute.ArgumentList.Arguments.First().Expression;
                    Optional<object?> constant = model.GetConstantValue(expression);
                    if (!constant.HasValue)
                    {
                        functionName = null;
                        return false;
                    }

                    functionName = constant.ToString();
                    return true;
                }
            }

            functionName = null;
            return false;
        }

        public static bool TryGetReturnType(MethodDeclarationSyntax method, out TypeSyntax returnTypeSyntax)
        {
            returnTypeSyntax = method.ReturnType;
            return true;
        }

        public static bool TryGetFunctionKind(MethodDeclarationSyntax method, out DurableFunctionKind kind)
        {
            SeparatedSyntaxList<ParameterSyntax> parameters = method.ParameterList.Parameters;

            foreach (ParameterSyntax parameterSyntax in parameters)
            {
                IEnumerable<AttributeSyntax> parameterAttributes = parameterSyntax.AttributeLists
                    .SelectMany(a => a.Attributes);
                foreach (AttributeSyntax attribute in parameterAttributes)
                {
                    if (attribute.ToString().Equals("OrchestrationTrigger", StringComparison.Ordinal))
                    {
                        kind = DurableFunctionKind.Orchestration;
                        return true;
                    }

                    if (attribute.ToString().Equals("ActivityTrigger", StringComparison.Ordinal))
                    {
                        kind = DurableFunctionKind.Activity;
                        return true;
                    }
                }
            }

            kind = DurableFunctionKind.Unknown;
            return false;
        }

        public static bool TryGetRequiredNamespaces(
            List<INamedTypeSymbol> types, out HashSet<string>? requiredNamespaces)
        {
            requiredNamespaces = new HashSet<string>();

            var remaining = new Queue<INamedTypeSymbol>(types);

            while (remaining.Count > 0)
            {
                INamedTypeSymbol typeInfo = remaining.Dequeue();
                if (typeInfo is null)
                {
                    return false;
                }

                if (typeInfo.ContainingNamespace.IsGlobalNamespace)
                {
                    requiredNamespaces = null;
                    return false;
                }

                requiredNamespaces.Add(typeInfo.ContainingNamespace.ToDisplayString());

                if (typeInfo.IsGenericType)
                {
                    foreach (ITypeSymbol typeArgument in typeInfo.TypeArguments)
                    {
                        if (typeArgument is INamedTypeSymbol named)
                        {
                            remaining.Enqueue(named);
                        }
                    }
                }
            }

            return true;
        }

        public static bool TryGetParameter(
            SemanticModel model,
            MethodDeclarationSyntax method,
            DurableFunctionKind kind,
            out TypedParameter? parameter)
        {
            foreach (ParameterSyntax methodParam in method.ParameterList.Parameters)
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
                            TypeInfo info = model.GetTypeInfo(methodParam.Type);
                            if (info.Type is INamedTypeSymbol named)
                            {
                                parameter = new TypedParameter(named, methodParam.Identifier.ToString());
                                return true;
                            }
                        }
                    }
                }
            }

            parameter = null;
            return false;
        }

        public static bool TryGetQualifiedTypeName(
            SemanticModel model, MethodDeclarationSyntax method, out string? fullTypeName)
        {
            ISymbol? symbol = model.GetEnclosingSymbol(method.SpanStart);
            if (symbol == null)
            {
                fullTypeName = null;
                return false;
            }

            fullTypeName = $@"{symbol.ToDisplayString()}.{method.Identifier}";
            return true;
        }

        public static string GetRenderedTypeExpression(ITypeSymbol? symbol, bool supportsNullable)
        {
            if (symbol == null)
            {
                return supportsNullable ? "object?" : "object";
            }

            if (supportsNullable && symbol.IsReferenceType
                && symbol.NullableAnnotation != NullableAnnotation.Annotated)
            {
                symbol = symbol.WithNullableAnnotation(NullableAnnotation.Annotated);
            }

            string expression = symbol.ToString();
            if (expression.StartsWith("System.", StringComparison.Ordinal)
                && symbol.ContainingNamespace.Name == "System")
            {
                expression = expression.Substring("System.".Length);
            }

            return expression;
        }

        static bool TryGetAttributeByName(
            MethodDeclarationSyntax method, string attributeName, out AttributeSyntax? attribute)
        {
            attribute = method.AttributeLists.SelectMany(a => a.Attributes).FirstOrDefault(
                a => a.Name.NormalizeWhitespace().ToFullString().Equals(attributeName, StringComparison.Ordinal));
            return attribute != null;
        }
    }
}
