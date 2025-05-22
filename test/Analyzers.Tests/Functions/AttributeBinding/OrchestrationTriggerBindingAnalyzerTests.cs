// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Analyzers.Functions.AttributeBinding;

namespace Dapr.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public class OrchestrationTriggerBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<OrchestrationTriggerBindingAnalyzer, OrchestrationTriggerBindingFixer>
{
    protected override string ExpectedDiagnosticId => OrchestrationTriggerBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[OrchestrationTrigger]";

    protected override string ExpectedType => "TaskOrchestrationContext";
}
