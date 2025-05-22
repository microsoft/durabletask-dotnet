// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Dapr.DurableTask.Analyzers.Orchestration;

namespace Dapr.DurableTask.Benchmarks;

/// <summary>
/// Compares the performance of the <see cref="Analyzers.Orchestration.DateTimeOrchestrationAnalyzer"/> against a compilation with no analyzers.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(DateTimeOrchestrationAnalyzerBenchmarkConfig))]
public class DateTimeOrchestrationAnalyzerBenchmarks
{
    SyntaxTree syntaxTree = null!;
    List<MetadataReference> references = null!;

    [Params(
        10,
        100,
        1000)]
    public int NumberOfClasses { get; set; }

    const string prefix = @"
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
";

    const string template = @"
public static class DurableFunction{0}
{{
    [Function(nameof(OrchestratorIsolated))]
    public static Task OrchestratorIsolated([OrchestrationTrigger] TaskOrchestrationContext context)
    {{
        DateTime now = DateTime.Now;
        return Task.CompletedTask;
    }}
}}
";

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Generate source code with the specified number of classes
        StringBuilder builder = new(prefix);
        for (int i = 0; i < this.NumberOfClasses; i++)
        {
            builder.AppendFormat(template, i);
        }
        string sourceCode = builder.ToString();

        // initialize the state used by all benchmarks
        this.syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        this.references = [
            // mscorlib
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            // netstandard
            MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location),
            // System.Runtime
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=6.0.0.0").Location),
            // Microsoft.Azure.Functions.Worker.Extensions.Abstractions
            MetadataReference.CreateFromFile(typeof(FunctionAttribute).Assembly.Location),
            // Microsoft.Azure.Functions.Worker.Extensions.DurableTask
            MetadataReference.CreateFromFile(typeof(OrchestrationTriggerAttribute).Assembly.Location),
            // Dapr.DurableTask.Abstractions
            MetadataReference.CreateFromFile(typeof(TaskOrchestrationContext).Assembly.Location),
        ];
    }

    [Benchmark]
    public void NoAnalyzer()
    {
        this.CreateCSharpCompilation()
            .GetDiagnostics();
    }

    [Benchmark(Baseline = true)]
    public async Task EmptyAnalyzer()
    {
        await this.CreateCSharpCompilation()
            .WithAnalyzers([new CustomEmptyAnalyzer()])
            .GetAllDiagnosticsAsync();
    }

    [Benchmark]
    public async Task DateTimeOrchestrationAnalyzer()
    {
        await this.CreateCSharpCompilation()
            .WithAnalyzers([new DateTimeOrchestrationAnalyzer()])
            .GetAllDiagnosticsAsync();
    }

    public CSharpCompilation CreateCSharpCompilation()
    {
        return CSharpCompilation
            .Create("MyAssembly", [this.syntaxTree], this.references)
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

#pragma warning disable RS1025,RS1026,RS1036 // roslyn best practices that don't apply here since we want an empty analyzer
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    class CustomEmptyAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [];

        public override void Initialize(AnalysisContext context) { }
    }
#pragma warning restore RS1025,RS1026,RS1036

    class DateTimeOrchestrationAnalyzerBenchmarkConfig : ManualConfig
    {
        public DateTimeOrchestrationAnalyzerBenchmarkConfig()
        {
            this.SummaryStyle = BenchmarkDotNet.Reports.SummaryStyle.Default
                .WithTimeUnit(Perfolizer.Horology.TimeUnit.Millisecond);
        }
    }
}
