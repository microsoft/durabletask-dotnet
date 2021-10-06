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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DurableTask.Generators
{
    static class Helpers
    {
        public static bool TryGetTypeName(GeneratorSyntaxContext? context, TypeSyntax type, out string? name)
        {
            // REVIEW: GenericNameSyntax?

            // Normal class names
            if (type is SimpleNameSyntax nameSyntax)
            {
                ITypeSymbol? typeInfo = context?.SemanticModel.GetTypeInfo(nameSyntax).Type;
                name = typeInfo?.ToDisplayString() ?? nameSyntax.Identifier.ValueText;
                return true;
            }

            // Built-in type names (int, string, etc.)
            if (type is PredefinedTypeSyntax typeSyntax)
            {
                name = typeSyntax.Keyword.ValueText;
                return true;
            }

            name = null;
            return false;
        }
    }
}
