// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Orchestration.ContinueAsNewOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class ContinueAsNewOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact(Skip = "Requires test infrastructure update to resolve TaskOrchestrationContext symbols correctly in CommonAssemblies")]
    public async Task TaskOrchestratorWhileTrueWithExternalEventNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        while (true)
        {
            var item = await context.WaitForExternalEvent<string>(""new-work"");
            await context.CallActivityAsync<string>(""ProcessItem"", item);
        }
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, test => test.CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None, expected);
    }

    [Fact(Skip = "Requires test infrastructure update to resolve TaskOrchestrationContext symbols correctly in CommonAssemblies")]
    public async Task TaskOrchestratorWhileTrueWithSubOrchestratorNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        while (true)
        {
            var item = await context.WaitForExternalEvent<string>(""new-work"");
            await context.CallSubOrchestratorAsync<string>(""ProcessItem"", item);
        }
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, test => test.CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None, expected);
    }

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithContinueAsNew_NoDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        int count = 0;
        while (true)
        {
            var item = await context.WaitForExternalEvent<string>(""new-work"");
            await context.CallSubOrchestratorAsync<string>(""ProcessItem"", item);
            count++;
            if (count >= 100)
            {
                context.ContinueAsNew(count);
                return null;
            }
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithOnlyActivitiesAndContinueAsNew_NoDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        while (true)
        {
            await context.CallActivityAsync<string>(""DoWork"", ""input"");
            await context.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
            context.ContinueAsNew(null);
            return null;
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorNoWhileTrue_NoDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        var result = await context.CallActivityAsync<string>(""DoWork"", input);
        return result;
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithOnlyActivitiesNoExternalEvent_NoDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        while (true)
        {
            await context.CallActivityAsync<string>(""DoWork"", ""input"");
            await context.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact(Skip = "Requires test infrastructure update to resolve TaskOrchestrationContext symbols correctly in CommonAssemblies")]
    public async Task DurableFunctionWhileTrueWithExternalEventNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task<object> Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    while (true)
    {
        var item = await context.WaitForExternalEvent<string>(""new-work"");
        await context.CallActivityAsync<string>(""ProcessItem"", item);
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, test => test.CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return new DiagnosticResult(ContinueAsNewOrchestrationAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }
}
