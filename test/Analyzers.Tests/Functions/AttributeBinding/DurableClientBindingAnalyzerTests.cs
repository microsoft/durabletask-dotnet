// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Analyzers.Functions.AttributeBinding;

namespace Dapr.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public class DurableClientBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<DurableClientBindingAnalyzer, DurableClientBindingFixer>
{
    protected override string ExpectedDiagnosticId => DurableClientBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[DurableClient]";

    protected override string ExpectedType => "DurableTaskClient";
}
