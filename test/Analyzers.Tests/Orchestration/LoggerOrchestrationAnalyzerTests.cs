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
    public async Task TaskOrchestratorPassingReplaySafeLoggerToHelperHasNoDiag()
    {
        // Repro for https://github.com/microsoft/durabletask-dotnet/issues/717:
        // a replay-safe logger passed to a helper method should not trigger DURABLE0010.
        string code = Wrapper.WrapTaskOrchestrator(@"
public record DemoInput(string Value);
public record DemoResult(string Value);

public class DemoOrchestrator : TaskOrchestrator<DemoInput, DemoResult>
{
    public override async Task<DemoResult> RunAsync(TaskOrchestrationContext context, DemoInput input)
    {
        var logger = context.CreateReplaySafeLogger(nameof(DemoOrchestrator));
        LogData(logger);
        return new DemoResult($""Processed: {input.Value}"");
    }

    private static void LogData(ILogger logger)
    {
        logger.LogInformation(""Logging some data for demonstration purposes."");
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorPassingReplaySafeLoggerThroughLocalAliasHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        ILogger alias = logger;
        Helper(alias);
        return Task.FromResult(""result"");
    }

    private static void Helper(ILogger logger)
    {
        logger.LogInformation(""Test"");
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorPassingFieldLoggerToHelperHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger logger;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        Helper({|#0:this.logger|});
        return Task.FromResult(""result"");
    }

    private static void Helper(ILogger logger)
    {
        {|#1:logger|}.LogInformation(""Test"");
    }
}
");

        DiagnosticResult expectedFieldRef = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");
        DiagnosticResult expectedParamRef = BuildDiagnostic().WithLocation(1).WithArguments("Helper", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expectedFieldRef, expectedParamRef);
    }

    [Fact]
    public async Task DurableFunctionOrchestrationPassingReplaySafeLoggerToHelperHasNoDiag()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    ILogger logger = context.CreateReplaySafeLogger(nameof(Run));
    Helper(logger);
}

void Helper(ILogger logger)
{
    logger.LogInformation(""Test"");
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorReplaySafeLoggerIntoLocalFunctionHelperHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        LogData(logger);
        return Task.FromResult(""result"");

        static void LogData(ILogger logger)
        {
            logger.LogInformation(""Test"");
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorHelperCalledWithMixedSafeAndUnsafeArgsHasDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger field;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger safe = context.CreateReplaySafeLogger<MyOrchestrator>();
        Helper(safe);
        Helper({|#0:this.field|});
        return Task.FromResult(""result"");
    }

    private static void Helper(ILogger logger)
    {
        {|#1:logger|}.LogInformation(""Test"");
    }
}
");

        DiagnosticResult expectedFieldRef = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");
        DiagnosticResult expectedParamRef = BuildDiagnostic().WithLocation(1).WithArguments("Helper", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expectedFieldRef, expectedParamRef);
    }

    [Fact]
    public async Task TaskOrchestratorTransitiveHelperChainHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        Outer(logger);
        return Task.FromResult(""result"");
    }

    private static void Outer(ILogger logger)
    {
        Inner(logger);
    }

    private static void Inner(ILogger logger)
    {
        logger.LogInformation(""Test"");
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorRecursiveHelperWithSafeLoggerHasNoDiag()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        Log(logger, 3);
        return Task.FromResult(""result"");
    }

    private static void Log(ILogger logger, int depth)
    {
        logger.LogInformation(""Test"");
        if (depth > 0)
        {
            Log(logger, depth - 1);
        }
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskOrchestratorConditionalAndCoalesceExpressionsForLoggerInitializer()
    {
        // Conditional with both branches safe → no diag.
        // Coalesce with a non-safe LHS → flagged (we cannot prove the LHS is null at runtime).
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger? maybeNull;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger a = context.CreateReplaySafeLogger<MyOrchestrator>();
        ILogger b = context.CreateReplaySafeLogger(nameof(MyOrchestrator));
        ILogger conditional = input.Length > 0 ? a : b;
        conditional.LogInformation(""ok"");

        ILogger coalesced = {|#0:this.maybeNull|} ?? a;
        {|#1:coalesced|}.LogInformation(""flagged"");

        return Task.FromResult(""result"");
    }
}
");

        DiagnosticResult expectedFieldRef = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");
        DiagnosticResult expectedLocalRef = BuildDiagnostic().WithLocation(1).WithArguments("RunAsync", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expectedFieldRef, expectedLocalRef);
    }

    [Fact]
    public async Task TaskOrchestratorFieldAssignedFromCreateReplaySafeLoggerIsStillFlagged()
    {
        // Documents an intentional limitation: even when a field is assigned from CreateReplaySafeLogger,
        // we treat field reads as unsafe because subsequent replays would re-execute the assignment
        // (potentially with a different context) and the field may also be mutated from elsewhere.
        // The assignment LHS itself is not flagged because it is write-only.
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    ILogger? logger;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        this.logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        {|#0:this.logger|}.LogInformation(""Test"");
        return Task.FromResult(""result"");
    }
}
");

        DiagnosticResult expected = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskOrchestratorLocalReassignedToUnsafeValueHasDiag()
    {
        // Even when a local is initialized from CreateReplaySafeLogger, a later reassignment to a
        // non-replay-safe value must flip the local to unsafe (flow-insensitive: any reassignment
        // reaches every subsequent read).
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger field;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        logger = {|#0:this.field|};
        {|#1:logger|}.LogInformation(""Test"");
        return Task.FromResult(""result"");
    }
}
");

        DiagnosticResult expected1 = BuildDiagnostic().WithLocation(0).WithArguments("RunAsync", "MyOrchestrator");
        DiagnosticResult expected2 = BuildDiagnostic().WithLocation(1).WithArguments("RunAsync", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected1, expected2);
    }

    [Fact]
    public async Task TaskOrchestratorHelperParameterReassignedToUnsafeValueHasDiag()
    {
        // Even when every call site passes a safe logger, a reassignment of the parameter inside
        // the helper to a non-replay-safe value must flip the parameter to unsafe.
        string code = Wrapper.WrapTaskOrchestrator(@"
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    readonly ILogger field;

    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        ILogger logger = context.CreateReplaySafeLogger<MyOrchestrator>();
        this.Helper(logger);
        return Task.FromResult(""result"");
    }

    private void Helper(ILogger logger)
    {
        logger = {|#0:this.field|};
        {|#1:logger|}.LogInformation(""Test"");
    }
}
");

        DiagnosticResult expected1 = BuildDiagnostic().WithLocation(0).WithArguments("Helper", "MyOrchestrator");
        DiagnosticResult expected2 = BuildDiagnostic().WithLocation(1).WithArguments("Helper", "MyOrchestrator");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected1, expected2);
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
