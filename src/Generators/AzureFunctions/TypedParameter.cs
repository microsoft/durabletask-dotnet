// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators.AzureFunctions
{
    public class TypedParameter(INamedTypeSymbol type, string name)
    {
        public INamedTypeSymbol Type { get; } = type;
        public string Name { get; } = name;

        public override string ToString()
        {
            return $"{SyntaxNodeUtility.GetRenderedTypeExpression(this.Type, false)} {this.Name}";
        }
    }
}
