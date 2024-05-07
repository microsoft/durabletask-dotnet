// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Analyzers.Functions.AttributeBinding;

namespace Microsoft.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public class OrchestrationTriggerBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<OrchestrationTriggerBindingAnalyzer>
{
    protected override string ExpectedDiagnosticId => OrchestrationTriggerBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[OrchestrationTrigger]";

    protected override string ExpectedType => "TaskOrchestrationContext";
}
