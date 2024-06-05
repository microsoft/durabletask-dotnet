// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;

/// <summary>
/// Code fixer for the <see cref="DurableClientBindingAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MatchingAttributeBindingFixer))]
[Shared]
public sealed class DurableClientBindingFixer : MatchingAttributeBindingFixer
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DurableClientBindingAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    public override string ExpectedType => "DurableTaskClient";
}
