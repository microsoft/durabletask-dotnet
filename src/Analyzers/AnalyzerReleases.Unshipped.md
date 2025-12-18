; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0009 | Orchestration | Info | **GetInputOrchestrationAnalyzer**: Suggests using input parameter binding instead of ctx.GetInput<T>() in orchestration methods.
