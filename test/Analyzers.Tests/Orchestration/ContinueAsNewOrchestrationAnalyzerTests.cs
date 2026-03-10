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

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithExternalEventNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        {|#0:while|} (true)
        {
            var item = await context.WaitForExternalEvent<string>(""new-work"");
            await context.CallActivityAsync<string>(""ProcessItem"", item);
        }
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithSubOrchestratorNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        {|#0:while|} (true)
        {
            await context.CallSubOrchestratorAsync<string>(""ProcessItem"", null);
        }
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorWhileTrueWithOnlyActivitiesNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<object, object>
{
    public override async Task<object> RunAsync(TaskOrchestrationContext context, object input)
    {
        {|#0:while|} (true)
        {
            await context.CallActivityAsync<string>(""DoWork"", ""input"");
            await context.CreateTimer(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
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
    public async Task TaskOrchestratorWhileTrueNoContextCalls_NoDiagnostic()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        int i = 0;
        while (true)
        {
            i++;
            if (i > 10) return ""done"";
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionWhileTrueWithExternalEventNoContinueAsNew_ReportsDiagnostic()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
async Task<object> Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:while|} (true)
    {
        var item = await context.WaitForExternalEvent<string>(""new-work"");
        await context.CallActivityAsync<string>(""ProcessItem"", item);
    }
}
");
        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(ContinueAsNewOrchestrationAnalyzer.DiagnosticId);
    }
}
