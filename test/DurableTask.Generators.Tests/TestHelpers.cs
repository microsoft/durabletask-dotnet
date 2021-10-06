// ----------------------------------------------------------------------------------
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace DurableTask.Generators.Tests;

static class TestHelpers
{
    // This needs to be kept up-to-date with the analyzer code.
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    public static Task RunTestAsync(string inputSource, string expectedOutputSource)
    {
        CSharpSourceGeneratorVerifier<ExtensionMethodGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { inputSource },
                GeneratedSources =
                {
                    (typeof(ExtensionMethodGenerator), GeneratedFileName, SourceText.From(expectedOutputSource, Encoding.UTF8, SourceHashAlgorithm.Sha256)),
                },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
            },
        };

        return test.RunAsync();
    }

    public static string WrapAndFormat(string methodList)
    {
        string formattedMethodList = IndentLines(spaces: 8, methodList);

        return $@"
// <generated />
#nullable enable

using System;
using System.Threading.Tasks;

namespace DurableTask
{{
    public static class {GeneratedClassName}
    {{
        {formattedMethodList.TrimStart()}
    }}
}}
".TrimStart();
    }

    static string IndentLines(int spaces, string multilineText)
    {
        string indent = new(' ', spaces);
        StringBuilder sb = new();

        foreach (string line in multilineText.Trim().Split(Environment.NewLine))
        {
            if (line.Length > 0)
            {
                sb.Append(indent);
            }

            sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }
}
