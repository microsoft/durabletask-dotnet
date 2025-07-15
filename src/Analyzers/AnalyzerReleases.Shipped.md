; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0001 | Orchestration | Warning | DateTimeOrchestrationAnalyzer
DURABLE0002 | Orchestration | Warning | GuidOrchestrationAnalyzer
DURABLE0003 | Orchestration | Warning | DelayOrchestrationAnalyzer
DURABLE0004 | Orchestration | Warning | ThreadTaskOrchestrationAnalyzer
DURABLE0005 | Orchestration | Warning | IOOrchestrationAnalyzer
DURABLE0006 | Orchestration | Warning | EnvironmentOrchestrationAnalyzer
DURABLE0007 | Orchestration | Warning | CancellationTokenOrchestrationAnalyzer
DURABLE0008 | Orchestration | Warning | OtherBindingsOrchestrationAnalyzer
DURABLE1001 | Attribute Binding | Error | OrchestrationTriggerBindingAnalyzer
DURABLE1002 | Attribute Binding | Error | DurableClientBindingAnalyzer
DURABLE1003 | Attribute Binding | Error | EntityTriggerBindingAnalyzer
DURABLE2001 | Activity | Warning | MatchingInputOutputTypeActivityAnalyzer
DURABLE2002 | Activity | Warning | MatchingInputOutputTypeActivityAnalyzer