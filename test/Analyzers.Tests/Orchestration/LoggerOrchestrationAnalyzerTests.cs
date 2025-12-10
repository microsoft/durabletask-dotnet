// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Orchestration;

using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Orchestration.LoggerOrchestrationAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Orchestration;

public class LoggerOrchestrationAnalyzerTests
{
    [Fact]
    public async Task EmptyCodeWithNoSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        // checks that empty code with no assembly references of Durable Functions has no diagnostics.
        // this guarantees that if someone adds our analyzer to a project that doesn't use Durable Functions,
        // the analyzer won't crash/they won't get any diagnostics
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Fact]
    public async Task EmptyCodeWithSymbolsAvailableHasNoDiag()
    {
        string code = @"";

        // checks that empty code with access to assembly references of Durable Functions has no diagnostics
        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task NonOrchestrationHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
void Method(ILogger logger){
    logger.LogInformation(""Test"");
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithLoggerParameterHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context, {|#0:ILogger logger|})
{
    logger.LogInformation(""Test"");
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithLoggerGenericParameterHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context, {|#0:ILogger<Orchestrator> logger|})
{
    logger.LogInformation(""Test"");
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithLoggerFieldReferenceHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
private readonly ILogger logger;

[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:this.logger|}.LogInformation(""Test"");
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationWithLoggerPropertyReferenceHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
private ILogger Logger { get; set; }

[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    {|#0:this.Logger|}.LogInformation(""Test"");
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Run", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationInvokingMethodWithLoggerHasDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
private readonly ILogger logger;

[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    Helper();
}

void Helper()
{
    {|#0:this.logger|}.LogInformation(""Test"");
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Helper", "Run");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationUsingContextLoggerHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    ILogger logger = context.CreateReplaySafeLogger(nameof(Run));
    logger.LogInformation(""Test"");
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorWithLoggerParameterHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger logger;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        {|#0:this.logger|}.LogInformation(""Test"");
        return Task.FromResult(""result"");
    }
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorUsingContextLoggerHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        logger.LogInformation(""Test"");
        return Task.FromResult(""result"");
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task FuncOrchestratorWithLoggerHasDiag()
    {
        string code = @"
using System;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    static ILogger staticLogger;

    public static void Main()
    {
        new ServiceCollection().AddDurableTaskWorker(builder =>
        {
            builder.AddTasks(tasks =>
            {
                tasks.AddOrchestratorFunc(""MyRun"", context =>
                {
                    {|#0:staticLogger|}.LogInformation(""Test"");
                    return ""result"";
                });
            });
        });
    }
}
";

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("Main", "MyRun");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task FuncOrchestratorUsingContextLoggerHasNoDiag()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", context =>
{
    ILogger logger = context.CreateReplaySafeLogger(""HelloSequence"");
    logger.LogInformation(""Test"");
    return ""result"";
});
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task ActivityFunctionWithLoggerHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""MyActivity"")]
string MyActivity([ActivityTrigger] string input, ILogger logger)
{
    logger.LogInformation(""Test"");
    return ""result"";
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(LoggerOrchestrationAnalyzer.DiagnosticId);
    }
}
