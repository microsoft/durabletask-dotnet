// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Analyzers.AttributeBinding;

namespace Microsoft.DurableTask.Analyzers.Tests.AttributeBinding;

public class EntityTriggerBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<EntityTriggerBindingAnalyzer>
{
    protected override string ExpectedDiagnosticId => EntityTriggerBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[EntityTrigger]";

    protected override string ExpectedType => "TaskEntityDispatcher";
}
