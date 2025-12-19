; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0009 | Orchestration | Info | **GetInputOrchestrationAnalyzer**: Suggests using input parameter binding instead of ctx.GetInput<T>() in orchestration methods.
DURABLE0010 | Orchestration | Warning | **LoggerOrchestrationAnalyzer**: Warns when a non-contextual ILogger is used in an orchestration method. Orchestrations should use `context.CreateReplaySafeLogger()` instead of injecting ILogger directly.
