; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.2.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE2003 | Activity | Warning | **FunctionNotFoundAnalyzer**: Warns when an activity function call references a name that does not match any defined activity in the compilation.
DURABLE2004 | Orchestration | Warning | **FunctionNotFoundAnalyzer**: Warns when a sub-orchestration call references a name that does not match any defined orchestrator in the compilation.

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DURABLE0001 | Orchestration | Warning | **DateTimeOrchestrationAnalyzer**: Warns when non-deterministic DateTime properties like DateTime.Now, DateTime.UtcNow, or DateTime.Today are used in orchestration methods. Use context.CurrentUtcDateTime instead to ensure deterministic replay and to follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp).
DURABLE0002 | Orchestration | Warning | **GuidOrchestrationAnalyzer**: Warns when Guid.NewGuid() is used in an orchestration method. This can break determinism. Please use context.NewGuid() for orchestration-safe GUID generation to follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp).
DURABLE0003 | Orchestration | Warning | **DelayOrchestrationAnalyzer**: Warns when Task.Delay or Thread.Sleep are used in orchestrations. These APIs are non-deterministic. Please use context.CreateTimer for delays instead to follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp).
DURABLE0004 | Orchestration | Warning | **ThreadTaskOrchestrationAnalyzer**: Warns on usage of non-deterministic thread and task APIs like Thread.Start, Task.Run, Task.ContinueWith, TaskFactory.StartNew in orchestrations. Orchestrations must not use parallelism APIs as these break replay and don't follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp).
DURABLE0005 | Orchestration | Warning | **IOOrchestrationAnalyzer**: Warns when I/O APIs (e.g., HttpClient, Azure Storage clients) are used directly in orchestrations. I/O calls are not replay-safe and should be invoked via activities to follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp)
DURABLE0006 | Orchestration | Warning | **EnvironmentOrchestrationAnalyzer**: Warns on usage of System.Environment APIs (e.g., GetEnvironmentVariable) in orchestrations. Reading environment variables can introduce non-determinism. Please follow [orchestrator code constraints](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp)
DURABLE0007 | Orchestration | Warning | **CancellationTokenOrchestrationAnalyzer**: Warns when CancellationToken parameters are used in orchestration function signatures. Orchestration methods should not accept cancellation tokens directly.
DURABLE0008 | Orchestration | Warning | **OtherBindingsOrchestrationAnalyzer**: Warns when orchestration methods have input parameters with bindings other than [OrchestrationTrigger] (e.g., [EntityTrigger], [DurableClient]). Orchestrations must only use [OrchestrationTrigger] bindings.
DURABLE1001 | Attribute Binding | Error | **OrchestrationTriggerBindingAnalyzer**: Ensures [OrchestrationTrigger] is only applied to parameters of type TaskOrchestrationContext.
DURABLE1002 | Attribute Binding | Error | **DurableClientBindingAnalyzer**: Ensures [DurableClient] is only applied to parameters of type DurableTaskClient.
DURABLE1003 | Attribute Binding | Error | **EntityTriggerBindingAnalyzer**: Ensures [EntityTrigger] is only applied to parameters of type TaskEntityDispatcher.
DURABLE2001 | Activity | Warning | **MatchingInputOutputTypeActivityAnalyzer**: Warns when the input type passed to an activity invocation does not match the activity's definition.
DURABLE2002 | Activity | Warning | **MatchingInputOutputTypeActivityAnalyzer**: Warns when the output type expected from an activity invocation does not match the activity's definition.
