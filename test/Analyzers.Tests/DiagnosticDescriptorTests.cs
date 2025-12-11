// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.DurableTask.Analyzers.Activities;
using Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;
using Microsoft.DurableTask.Analyzers.Functions.Orchestration;
using Microsoft.DurableTask.Analyzers.Orchestration;

namespace Microsoft.DurableTask.Analyzers.Tests;

/// <summary>
/// Tests to validate diagnostic descriptor properties.
/// </summary>
public class DiagnosticDescriptorTests
{
    [Theory]
    [InlineData(typeof(DateTimeOrchestrationAnalyzer))]
    [InlineData(typeof(GuidOrchestrationAnalyzer))]
    [InlineData(typeof(DelayOrchestrationAnalyzer))]
    [InlineData(typeof(ThreadTaskOrchestrationAnalyzer))]
    [InlineData(typeof(IOOrchestrationAnalyzer))]
    [InlineData(typeof(EnvironmentOrchestrationAnalyzer))]
    [InlineData(typeof(CancellationTokenOrchestrationAnalyzer))]
    [InlineData(typeof(OtherBindingsOrchestrationAnalyzer))]
    [InlineData(typeof(OrchestrationTriggerBindingAnalyzer))]
    [InlineData(typeof(DurableClientBindingAnalyzer))]
    [InlineData(typeof(EntityTriggerBindingAnalyzer))]
    [InlineData(typeof(MatchingInputOutputTypeActivityAnalyzer))]
    [InlineData(typeof(FunctionNotFoundAnalyzer))]
    public void AllDiagnosticDescriptorsHaveHelpLinkUri(Type analyzerType)
    {
        // Arrange
        DiagnosticAnalyzer? analyzer = Activator.CreateInstance(analyzerType) as DiagnosticAnalyzer;
        Assert.NotNull(analyzer);

        ImmutableArray<Microsoft.CodeAnalysis.DiagnosticDescriptor> diagnostics = analyzer!.SupportedDiagnostics;
        Assert.NotEmpty(diagnostics);

        // Act & Assert
        foreach (Microsoft.CodeAnalysis.DiagnosticDescriptor diagnostic in diagnostics)
        {
            Assert.NotNull(diagnostic.HelpLinkUri);
            Assert.NotEmpty(diagnostic.HelpLinkUri);
            Assert.StartsWith("https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp#", diagnostic.HelpLinkUri);
            Assert.EndsWith(diagnostic.Id, diagnostic.HelpLinkUri);
        }
    }
}
