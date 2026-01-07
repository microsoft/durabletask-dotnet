; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE3001 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when a task name in [DurableTask] attribute is not a valid C# identifier. Task names must start with a letter or underscore and contain only letters, digits, and underscores.
DURABLE3002 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when an event name in [DurableEvent] attribute is not a valid C# identifier. Event names must start with a letter or underscore and contain only letters, digits, and underscores.
