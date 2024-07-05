// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Microsoft.DurableTask.Analyzers.Activities;
using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Activities.MatchingInputOutputTypeActivityAnalyzer>;

namespace Microsoft.DurableTask.Analyzers.Tests.Activities;

public class MatchingInputOutputTypeActivityAnalyzerTests
{
    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingInputType()
    {
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

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMismatchedInputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(nameof(SayHello), 123456)|};
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");
        DiagnosticResult expected = BuildInputDiagnostic().WithLocation(0).WithArguments("int", "string", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMissingInputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(nameof(SayHello))|};
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");
        DiagnosticResult expected = BuildInputDiagnostic().WithLocation(0).WithArguments("none", "string", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingOutputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    int output = await {|#0:context.CallActivityAsync<int>(nameof(SayHello), ""Tokyo"")|};
}

[Function(nameof(SayHello))]
int SayHello([ActivityTrigger] string name)
{
    return 42;
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingTaskTOutputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    int output = await {|#0:context.CallActivityAsync<int>(nameof(SayHello), ""Tokyo"")|};
}

[Function(nameof(SayHello))]
Task<int> SayHello([ActivityTrigger] string name)
{
    return Task.FromResult(42);
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingVoidOutputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(nameof(SayHello), ""Tokyo"")|};
}

[Function(nameof(SayHello))]
void SayHello([ActivityTrigger] string name)
{
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMatchingTaskOutputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await {|#0:context.CallActivityAsync(nameof(SayHello), ""Tokyo"")|};
}

[Function(nameof(SayHello))]
Task SayHello([ActivityTrigger] string name)
{
    return Task.CompletedTask;
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task DurableFunctionActivityInvocationWithMismatchedOutputType()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    string output = await {|#0:context.CallActivityAsync<string>(nameof(SayHello), ""Tokyo"")|};
}

[Function(nameof(SayHello))]
int SayHello([ActivityTrigger] string name)
{
    return 42;
}
");

        DiagnosticResult expected = BuildOutputDiagnostic().WithLocation(0).WithArguments("string", "int", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }


    [Fact]
    public async Task TaskActivityInvocationWithMatchingInputTypeAndOutputType()
    {
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

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task TaskActivityInvocationWithMismatchedInputType()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class Caller {
    async Task Method(TaskOrchestrationContext context)
    {
        await {|#0:context.CallActivityAsync<string>(nameof(MyActivity), ""Tokyo"")|};
    }
}

public class MyActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int cityCode)
    {
        return Task.FromResult(cityCode.ToString());
    }
}
");

        DiagnosticResult expected = BuildInputDiagnostic().WithLocation(0).WithArguments("string", "int", "MyActivity");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskActivityInvocationWithMismatchedOutputType()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class Caller {
    async Task Method(TaskOrchestrationContext context)
    {
        await {|#0:context.CallActivityAsync<string>(nameof(MyActivity), ""Tokyo"")|};
    }
}

public class MyActivity : TaskActivity<string, int>
{
    public override Task<int> RunAsync(TaskActivityContext context, string city)
    {
        return Task.FromResult(city.Length);
    }
}
");

        DiagnosticResult expected = BuildOutputDiagnostic().WithLocation(0).WithArguments("string", "int", "MyActivity");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task TaskActivityInvocationWithOneTypeParameterDefinedInAbstractClass()
    {
        string code = Wrapper.WrapTaskOrchestrator(@"
public class Caller {
    async Task Method(TaskOrchestrationContext context)
    {
        await context.CallActivityAsync<int>(nameof(AnotherActivity), 5);
    }
}

public class AnotherActivity : EchoActivity<int> { }

public abstract class EchoActivity<T> : TaskActivity<T, T>
{
    public override Task<T> RunAsync(TaskActivityContext context, T input)
    {
        return Task.FromResult(input);
    }
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }


    [Fact]
    public async Task LambdaActivityInvocationWithMatchingInputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await context.CallActivityAsync(""SayHello"", ""Tokyo""));

tasks.AddActivityFunc<string>(""SayHello"", (context, city) => { });
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMatchingNoInputTypeAndNoOutputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await context.CallActivityAsync(""SayHello""));

tasks.AddActivityFunc(""SayHello"", (context) => { });
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMismatchedInputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await {|#0:context.CallActivityAsync(""SayHello"", 42)|});

tasks.AddActivityFunc<string>(""SayHello"", (context, city) => { });
");

        DiagnosticResult expected = BuildInputDiagnostic().WithLocation(0).WithArguments("int", "string", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMismatchedNoInputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await {|#0:context.CallActivityAsync(""SayHello"", ""Tokyo"")|});

tasks.AddActivityFunc(""SayHello"", (context) => { });
");

        DiagnosticResult expected = BuildInputDiagnostic().WithLocation(0).WithArguments("string", "none", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMatchingOutputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await context.CallActivityAsync<string>(""SayHello""));

tasks.AddActivityFunc<string>(""SayHello"", (context) => ""hello"");
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMismatchedOutputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await {|#0:context.CallActivityAsync<int>(""SayHello"")|});

tasks.AddActivityFunc<string>(""SayHello"", (context) => ""hello"");
");

        DiagnosticResult expected = BuildOutputDiagnostic().WithLocation(0).WithArguments("int", "string", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task LambdaActivityInvocationWithMismatchedNoOutputType()
    {
        string code = Wrapper.WrapFuncOrchestrator(@"
tasks.AddOrchestratorFunc(""HelloSequence"", async context =>
    await {|#0:context.CallActivityAsync<int>(""SayHello"")|});

tasks.AddActivityFunc(""SayHello"", (context) => { });
");

        DiagnosticResult expected = BuildOutputDiagnostic().WithLocation(0).WithArguments("int", "none", "SayHello");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }


    [Fact]
    public async Task ActivityInvocationWithConstantNameIsDiscovered()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
const string name = ""SayHello"";

async Task Method(TaskOrchestrationContext context)
{
    // the ones containing the output mismatch diagnostic mean they were discovered
    await {|#0:context.CallActivityAsync<string>(""SayHello"", ""Tokyo"")|};
    await {|#1:context.CallActivityAsync<string>(nameof(SayHello), ""Tokyo"")|};
    await {|#2:context.CallActivityAsync<string>(name, ""Tokyo"")|};

    // not diagnostics here, because the name could not be determined (since it is not a constant)
    string anotherName = ""SayHello"";
    await context.CallActivityAsync<string>(anotherName, ""Tokyo"");
}

[Function(nameof(SayHello))]
int SayHello([ActivityTrigger] string name) => 42;
");

        DiagnosticResult[] expected = Enumerable.Range(0, 3).Select(i =>
            BuildOutputDiagnostic().WithLocation(i).WithArguments("string", "int", "SayHello")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    [Fact]
    public async Task ActivityInvocationWithNonExistentActivity()
    {
        // When the Activity is not found, we cannot correlate this invocation to an existent activity in compile time
        // or it is defined in another assembly. We could add a diagnostic here if we want to enforce that,
        // but while we experiment with this analyzer, we will not report a diagnostic to prevent false positives.

        string code = Wrapper.WrapDurableFunctionOrchestration(@"
async Task Method(TaskOrchestrationContext context)
{
    await context.CallActivityAsync<string>(""ActivityNotFound"", ""Tokyo"");
}
");

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }


    static DiagnosticResult BuildInputDiagnostic()
    {
        return VerifyCS.Diagnostic(MatchingInputOutputTypeActivityAnalyzer.InputArgumentTypeMismatchDiagnosticId);
    }

    static DiagnosticResult BuildOutputDiagnostic()
    {
        return VerifyCS.Diagnostic(MatchingInputOutputTypeActivityAnalyzer.OutputArgumentTypeMismatchDiagnosticId);
    }
}
