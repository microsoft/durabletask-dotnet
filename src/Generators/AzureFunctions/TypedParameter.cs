// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators.AzureFunctions
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
            // Use the type as-is, preserving the nullability annotation from the source
            string typeExpression = SyntaxNodeUtility.GetRenderedTypeExpression(this.Type, false);
            
            // Special case: if the type is exactly System.Object (not a nullable object), make it nullable
            // This is because object parameters are typically nullable in the context of Durable Functions
            if (this.Type.SpecialType == SpecialType.System_Object && this.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                typeExpression = "object?";
            }
            
            return $"{typeExpression} {this.Name}";
        }
    }
}
