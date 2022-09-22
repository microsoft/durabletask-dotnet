// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DurableTask.Generators.AzureFunctions
{
    public class TypedParameter
    {
        public TypeSyntax Type { get; }
        public string ResolvedType { get; }
        public string Name { get; }

        public TypedParameter(TypeSyntax type, string resolvedType, string name)
        {
            this.Type = type;
            this.ResolvedType = resolvedType;
            this.Name = name;
        }

        public override string ToString()
        {
            return $"{this.ResolvedType} {this.Name}";
        }
    }
}
