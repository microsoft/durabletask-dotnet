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

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DurableTask.Generators.AzureFunctions
{
    public class TypedParameter
    {
        public TypeSyntax Type { get; }
        public string Name { get; }

        public TypedParameter(TypeSyntax type, string name)
        {
            this.Type = type;
            this.Name = name;
        }

        public override string ToString()
        {
            return $"{this.Type} {this.Name}";
        }
    }
}
