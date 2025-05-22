// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Dapr.DurableTask.Analyzers.Functions.AttributeBinding;

namespace Dapr.DurableTask.Analyzers.Tests.Functions.AttributeBinding;

public class EntityTriggerBindingAnalyzerTests : MatchingAttributeBindingSpecificationTests<EntityTriggerBindingAnalyzer, EntityTriggerBindingFixer>
{
    protected override string ExpectedDiagnosticId => EntityTriggerBindingAnalyzer.DiagnosticId;

    protected override string ExpectedAttribute => "[EntityTrigger]";

    protected override string ExpectedType => "TaskEntityDispatcher";
}
