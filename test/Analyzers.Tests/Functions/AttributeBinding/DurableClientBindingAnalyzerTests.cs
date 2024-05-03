// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;

namespace Microsoft.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public class DurableClientBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<DurableClientBindingAnalyzer>
{
    protected override string ExpectedDiagnosticId => DurableClientBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[DurableClient]";

    protected override string ExpectedType => "DurableTaskClient";
}
