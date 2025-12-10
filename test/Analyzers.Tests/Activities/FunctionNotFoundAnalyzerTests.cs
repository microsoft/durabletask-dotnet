// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Activities;
using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Activities.FunctionNotFoundAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Activities;

public class FunctionNotFoundAnalyzerTests
{
    // ==================== Activity Tests ====================

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingActivity_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await context.CallActivityAsync(nameof(SayHello), ""Tokyo"");
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMismatchedName_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(""SayHallo"", ""Tokyo"")|};
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");
        DiagnosticResult expected = BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("SayHallo");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithNonExistentActivity_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(""NonExistentActivity"", ""Tokyo"")|};
}
");
        DiagnosticResult expected = BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("NonExistentActivity");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskActivityInvocationWithMatchingClassBasedActivity_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapTaskOrchestrator(@"
public class Caller {
    async Task Method(TaskOrchestrationContext context)
    {
        await context.CallActivityAsync<string>(nameof(MyActivity), ""Tokyo"");
    }
}

public class MyActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string cityName)
    {
        return Task.FromResult(cityName);
    }
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskActivityInvocationWithMismatchedName_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapTaskOrchestrator(@"
public class Caller {
    async Task Method(TaskOrchestrationContext context)
    {
        await {|#0:context.CallActivityAsync<string>(""MyActiviti"", ""Tokyo"")|};
    }
}

public class MyActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string cityName)
    {
        return Task.FromResult(cityName);
    }
}
");
        DiagnosticResult expected = BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("MyActiviti");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMatchingActivity_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await context.CallActivityAsync(""SayHello"", ""Tokyo""));

tasks.AddActivityFunc<string>(""SayHello"", (context, city) => { });
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMismatchedName_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await {|#0:context.CallActivityAsync(""SayHallo"", ""Tokyo"")|});

tasks.AddActivityFunc<string>(""SayHello"", (context, city) => { });
");
        DiagnosticResult expected = BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("SayHallo");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task ActivityInvocationWithConstVariableName_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
const string activityName = ""WrongActivityName"";

async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(activityName, ""Tokyo"")|};
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");
        DiagnosticResult expected = BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("WrongActivityName");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task ActivityInvocationWithNonConstVariable_NoDiagnostic()
    {
        // Arrange - When using a non-const variable, we cannot determine the value at compile-time
        // so no diagnostic is reported (to avoid false positives)
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    string activityName = ""NonExistentActivity"";
    await context.CallActivityAsync(activityName, ""Tokyo"");
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    // ==================== Sub-Orchestration Tests ====================

    [Fact]
    public async Task SubOrchestrationInvocationWithMatchingOrchestration_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await context.CallSubOrchestratorAsync(nameof(ChildOrchestration), ""input"");
}

[Function(nameof(ChildOrchestration))]
async Task ChildOrchestration([OrchestrationTrigger] TaskOrchestrationContext context)
{
    await Task.CompletedTask;
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithMismatchedName_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallSubOrchestratorAsync(""ChildOrchestration_WrongName"", ""input"")|};
}

[Function(nameof(ChildOrchestration))]
async Task ChildOrchestration([OrchestrationTrigger] TaskOrchestrationContext context)
{
    await Task.CompletedTask;
}
");
        DiagnosticResult expected = BuildSubOrchestrationNotFoundDiagnostic().WithLocation(0).WithArguments("ChildOrchestration_WrongName");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithNonExistentOrchestrator_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallSubOrchestratorAsync(""NonExistentOrchestrator"", ""input"")|};
}
");
        DiagnosticResult expected = BuildSubOrchestrationNotFoundDiagnostic().WithLocation(0).WithArguments("NonExistentOrchestrator");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithClassBasedOrchestrator_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapTaskOrchestrator(@"
public class ParentOrchestration : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        await context.CallSubOrchestratorAsync<string>(nameof(ChildOrchestration), ""input"");
        return ""done"";
    }
}

public class ChildOrchestration : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        return Task.FromResult(input);
    }
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithLambdaOrchestrator_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""ParentOrchestration"", async context =>
    await context.CallSubOrchestratorAsync(""ChildOrchestration"", ""input""));

tasks.AddOrchestratorFunc(""ChildOrchestration"", context => Task.CompletedTask);
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithMismatchedLambdaOrchestrator_ReportsDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""ParentOrchestration"", async context =>
    await {|#0:context.CallSubOrchestratorAsync(""ChildOrchestration_WrongName"", ""input"")|});

tasks.AddOrchestratorFunc(""ChildOrchestration"", context => Task.CompletedTask);
");
        DiagnosticResult expected = BuildSubOrchestrationNotFoundDiagnostic().WithLocation(0).WithArguments("ChildOrchestration_WrongName");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task SubOrchestrationInvocationWithTypedResult_NoDiagnostic()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    int result = await context.CallSubOrchestratorAsync<int>(nameof(ChildOrchestration), ""input"");
}

[Function(nameof(ChildOrchestration))]
async Task<int> ChildOrchestration([OrchestrationTrigger] TaskOrchestrationContext context)
{
    return 42;
}
");

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task MultipleInvocationsWithSomeMissingFunctions_ReportsMultipleDiagnostics()
    {
        // Arrange
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await context.CallActivityAsync(nameof(ExistingActivity), ""input"");
    await {|#0:context.CallActivityAsync(""MissingActivity"", ""input"")|};
    await context.CallSubOrchestratorAsync(nameof(ExistingOrchestrator), ""input"");
    await {|#1:context.CallSubOrchestratorAsync(""MissingOrchestrator"", ""input"")|};
}

[Function(nameof(ExistingActivity))]
void ExistingActivity([ActivityTrigger] string name) { }

[Function(nameof(ExistingOrchestrator))]
async Task ExistingOrchestrator([OrchestrationTrigger] TaskOrchestrationContext context)
{
    await Task.CompletedTask;
}
");
        DiagnosticResult[] expected =
        [
            BuildActivityNotFoundDiagnostic().WithLocation(0).WithArguments("MissingActivity"),
            BuildSubOrchestrationNotFoundDiagnostic().WithLocation(1).WithArguments("MissingOrchestrator")
        ];

        // Act & Assert
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildActivityNotFoundDiagnostic()
    {
        return VerifyCS.Diagnostic(FunctionNotFoundAnalyzer.ActivityNotFoundDiagnosticId);
    }

    static DiagnosticResult BuildSubOrchestrationNotFoundDiagnostic()
    {
        return VerifyCS.Diagnostic(FunctionNotFoundAnalyzer.SubOrchestrationNotFoundDiagnosticId);
    }
}
