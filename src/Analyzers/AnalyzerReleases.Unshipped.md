; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0009 | Orchestration | Warning | **LoggerOrchestrationAnalyzer**: Warns when a non-contextual ILogger is used in an orchestration method. Orchestrations should use `context.CreateReplaySafeLogger()` instead of injecting ILogger directly.
DURABLE2003 | Activity | Warning | **FunctionNotFoundAnalyzer**: Warns when an activity function call references a name that does not match any defined activity in the compilation.
DURABLE2004 | Orchestration | Warning | **FunctionNotFoundAnalyzer**: Warns when a sub-orchestration call references a name that does not match any defined orchestrator in the compilation.