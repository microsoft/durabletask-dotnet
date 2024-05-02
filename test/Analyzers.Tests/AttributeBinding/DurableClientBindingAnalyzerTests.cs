// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Analyzers.AttributeBinding;

namespace Microsoft.DurableTask.Analyzers.Tests.AttributeBinding;

public class DurableClientBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<DurableClientBindingAnalyzer>
{
    protected override string ExpectedDiagnosticId => DurableClientBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[DurableClient]";

    protected override string ExpectedType => "DurableTaskClient";
}
