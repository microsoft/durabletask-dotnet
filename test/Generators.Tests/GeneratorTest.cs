// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.DurableTask.Generators.Tests;

public abstract class GeneratorTest<TGenerator>
    where TGenerator : IIncrementalGenerator, new()
{
    protected virtual VerifySettings Settings()
    {
        VerifySettings settings = new();
        settings.IgnoreEmbeddedAttribute();
        return settings;
    }

    protected virtual MetadataReference[] GetMetadataReferences()
    {
        // By default we will add all of our assemblies and then Microsoft.DurableTask.Abstractions.
        return
        [
            ..AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location)),
            MetadataReference.CreateFromFile(typeof(DurableTaskAttribute).Assembly.Location),
        ];
    }

    protected virtual GeneratorDriver BuildDriver(params string[] sources)
    {
        // Parse the provided classes into a C# syntax tree
        SyntaxTree[] syntaxTrees = [.. sources.Select(classSource => CSharpSyntaxTree.ParseText(classSource))];

        // Create a Roslyn compilation for the syntax tree.
        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: syntaxTrees,
            references: this.GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        TGenerator generator = new();
        return CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
    }
}
