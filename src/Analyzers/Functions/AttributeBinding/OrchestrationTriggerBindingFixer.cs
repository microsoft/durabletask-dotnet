// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Dapr.DurableTask.Analyzers.Functions.AttributeBinding;

/// <summary>
/// Code fixer for the <see cref="OrchestrationTriggerBindingAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MatchingAttributeBindingFixer))]
[Shared]
public sealed class OrchestrationTriggerBindingFixer : MatchingAttributeBindingFixer
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [OrchestrationTriggerBindingAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    public override string ExpectedType => "TaskOrchestrationContext";
}
