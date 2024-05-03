; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0001 | Orchestration | Warning | DateTimeOrchestrationAnalyzer
DURABLE0002 | Orchestration | Warning | GuidOrchestrationAnalyzer
DURABLE0003 | Orchestration | Warning | DelayOrchestrationAnalyzer
DURABLE1001 | Attribute Binding | Error | OrchestrationTriggerBindingAnalyzer
DURABLE1002 | Attribute Binding | Error | DurableClientBindingAnalyzer
DURABLE1003 | Attribute Binding | Error | EntityTriggerBindingAnalyzer