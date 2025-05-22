// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using Microsoft.CodeAnalysis;

namespace Dapr.DurableTask.Generators.AzureFunctions
{
    public class TypedParameter
    {
        public INamedTypeSymbol Type { get; }
        public string Name { get; }

        public TypedParameter(INamedTypeSymbol type, string name)
        {
            this.Type = type;
            this.Name = name;
        }

        public override string ToString()
        {
            return $"{SyntaxNodeUtility.GetRenderedTypeExpression(this.Type, false)} {this.Name}";
        }
    }
}
