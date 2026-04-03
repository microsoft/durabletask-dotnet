; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE3001 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when a task name in [DurableTask] attribute is not a valid C# identifier. Task names must start with a letter or underscore and contain only letters, digits, and underscores.
DURABLE3002 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when an event name in [DurableEvent] attribute is not a valid C# identifier. Event names must start with a letter or underscore and contain only letters, digits, and underscores.
DURABLE3003 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when a standalone project declares the same orchestrator or activity logical name and version more than once.
DURABLE3004 | DurableTask.Design | Error | **DurableTaskSourceGenerator**: Reports when an Azure Functions project declares multiple class-based orchestrators or activities with the same logical durable task name.
