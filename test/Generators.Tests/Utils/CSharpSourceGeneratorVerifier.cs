// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Microsoft.DurableTask.Generators.Tests.Utils;

// Mostly copy/pasted from the Microsoft Source Generators testing documentation
public static class CSharpSourceGeneratorVerifier<TSourceGenerator> where TSourceGenerator : IIncrementalGenerator, new()
{
    public class Test : CSharpSourceGeneratorTest<TSourceGenerator, DefaultVerifier>
    {
        public Test()
        {
            // See https://www.nuget.org/packages/Microsoft.NETCore.App.Ref/8.0.22
            this.ReferenceAssemblies = new ReferenceAssemblies(
                targetFramework: "net8.0",
                referenceAssemblyPackage: new PackageIdentity("Microsoft.NETCore.App.Ref", "8.0.22"),
                referenceAssemblyPath: Path.Combine("ref", "net8.0"));
        }

        public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.CSharp9;

        protected override CompilationOptions CreateCompilationOptions()
        {
            CompilationOptions compilationOptions = base.CreateCompilationOptions();
            return compilationOptions.WithSpecificDiagnosticOptions(
                 compilationOptions.SpecificDiagnosticOptions.SetItems(GetNullableWarningsFromCompiler()));
        }

        static ImmutableDictionary<string, ReportDiagnostic> GetNullableWarningsFromCompiler()
        {
            string[] args = { "/warnaserror:nullable" };
            CSharpCommandLineArguments commandLineArguments = CSharpCommandLineParser.Default.Parse(
                args,
                baseDirectory: Environment.CurrentDirectory,
                sdkDirectory: Environment.CurrentDirectory);
            ImmutableDictionary<string, ReportDiagnostic> nullableWarnings = commandLineArguments.CompilationOptions.SpecificDiagnosticOptions;

            return nullableWarnings;
        }

        protected override ParseOptions CreateParseOptions()
        {
            return ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(this.LanguageVersion);
        }
    }
}
