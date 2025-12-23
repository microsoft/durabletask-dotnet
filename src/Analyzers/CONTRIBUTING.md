# Contributing to DurableTask.Analyzers

This document presents guidelines on how to contribute and develop `DurableTask.Analyzers`,
a series of [Roslyn Analyzers](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview?view=vs-2022)
that inspect Durable Task and Azure Functions code for quality, maintainability, code constraints, and other issues. 

## Requirements

- [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Building

If you are using Visual Studio, you can just open the main [solution file](../../Microsoft.DurableTask.sln)
and be able to build the [project](./Analyzers.csproj). 

In case you are developing from another IDE or want to build using the command line:

```shell
dotnet build
```

You can also run unit tests from the command line using: 

```shell
dotnet test ../../test/Analyzers.Tests/
```

## Developing Roslyn Analyzers

Currently, there are 3 diagnostics [categories](./AnalyzersCategories.cs):

| Category          | Diagnostic Id Prefix | Description |
|-------------------|----------------------| ------------|
| Orchestration     | DURABLE0xxx          | Diagnostics that are reported when a [non-deterministic code constraint](https://learn.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-code-constraints?tabs=csharp) occurs. |
| Attribute Binding | DURABLE1xxx          | A specific category that only affects Azure Durable Functions. It groups diagnostics that check whether the right C# Attribute is being used with the right type. |
| Activity          | DURABLE2xxx          | Diagnostics that are reported when an activity is called using wrong parameters. |

So, when developing a new analyzer, you must either select one of those existing categories or create a new one.

The following resources are useful to start developing Roslyn Analyzers and understanding their APIs:

- [Tutorial: Write your first analyzer and code fix](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- [Roslyn analyzers and code-aware library for ImmutableArrays](https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-analyzers-and-code-aware-library-for-immutablearrays?view=vs-2022)
- [Roslyn code samples](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/Analyzer%20Samples.md)

### Orchestration Analysis and Method Probing

Many of the orchestration analyzers need to understand which methods in a codebase are orchestration entry points and which methods are invoked by orchestrations (directly or indirectly). This section documents how orchestrations are discovered and how method call chains are analyzed.

#### How Orchestrations Are Discovered

The `OrchestrationAnalyzer<TOrchestrationVisitor>` base class provides a unified framework for discovering orchestrations across three different patterns:

##### 1. Durable Functions Orchestrations

Durable Functions orchestrations are discovered by looking for methods with the following characteristics:
- The method has a parameter decorated with `[OrchestrationTrigger]` attribute from `Microsoft.Azure.Functions.Worker` namespace
- The method or class has a `[Function]` attribute with a function name

**Example:**
```cs
[Function("MyOrchestrator")]
public async Task Run([OrchestrationTrigger] TaskOrchestrationContext context)
{
    // orchestration logic
}
```

The analyzer registers a `SyntaxNodeAction` for `SyntaxKind.MethodDeclaration` and checks if the method symbol contains the `OrchestrationTriggerAttribute` in any of its parameters.

##### 2. TaskOrchestrator Orchestrations

TaskOrchestrator orchestrations are discovered by looking for classes that:
- Implement the `ITaskOrchestrator` interface, or
- Inherit from the `TaskOrchestrator<TInput, TOutput>` base class

**Example:**
```cs
public class MyOrchestrator : TaskOrchestrator<string, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        // orchestration logic
    }
}
```

The analyzer registers a `SyntaxNodeAction` for `SyntaxKind.ClassDeclaration` and checks if the class implements `ITaskOrchestrator`. It then looks for methods that have a `TaskOrchestrationContext` parameter.

##### 3. Func Orchestrations

Func orchestrations are discovered by looking for calls to `DurableTaskRegistry.AddOrchestratorFunc()` extension methods where an orchestration is registered as a lambda or method reference.

**Example:**
```cs
tasks.AddOrchestratorFunc("HelloSequence", context =>
{
    // orchestration logic
    return "Hello";
});
```

The analyzer registers an `OperationAction` for `OperationKind.Invocation` and checks if:
- The invocation returns a `DurableTaskRegistry` type
- The method name is `AddOrchestratorFunc`
- The `orchestrator` parameter is a delegate (lambda or method reference)

#### How Method Probing Works

The `MethodProbeOrchestrationVisitor` class implements recursive method call analysis to detect violations in methods invoked by orchestrations. This is crucial because non-deterministic code can exist not just in the orchestration entry point, but also in helper methods called by the orchestration.

##### Probing Algorithm

1. **Entry Point**: When an orchestration is discovered, the visitor starts analyzing from the orchestration's root method.

2. **Recursive Traversal**: For each method:
   - The method is added to a dictionary that tracks which orchestrations invoke it
   - All `InvocationExpressionSyntax` nodes within the method body are examined
   - For each invocation, the target method is identified via semantic analysis
   - The target method is recursively analyzed with the same orchestration name

3. **Cycle Detection**: The analyzer maintains a `ConcurrentDictionary<IMethodSymbol, ConcurrentBag<string>>` that maps methods to the orchestrations that invoke them. Before analyzing a method for a specific orchestration, it checks if that combination has already been processed to prevent infinite recursion.

4. **Cross-Tree Analysis**: When a called method is defined in a different syntax tree (e.g., in another file or partial class), the analyzer obtains the correct `SemanticModel` for that syntax tree to continue analysis.

##### Probing Capabilities

The method probing can follow:
- **Direct method calls**: `Helper()` where `Helper` is a concrete method
- **Static methods**: `MyClass.StaticHelper()`
- **Instance methods**: `myInstance.Method()`
- **Method chains**: Calls through multiple levels (`Method1()` → `Method2()` → `Method3()`)
- **Async methods**: Methods returning `Task` or `ValueTask`
- **Lambda expressions**: Inline lambdas and local functions within orchestrations
- **Method references**: Delegates created from method references
- **Partial classes**: Methods defined across multiple files via partial class declarations
- **Recursive methods**: Protected against infinite loops via cycle detection

##### Probing Limitations

The method probing **cannot** follow method calls in the following scenarios:

1. **Interface Method Calls**: When calling a method through an interface reference, the analyzer cannot determine which implementation will be invoked at runtime.

   ```cs
   // Will NOT be analyzed
   IHelper helper = GetHelper(); // implementation unknown
   helper.DoSomething(); // analyzer cannot follow this call
   ```

2. **Abstract Method Calls**: Similar to interfaces, abstract method implementations are not known at analysis time.

   ```cs
   // Will NOT be analyzed
   abstract class Base {
       public abstract void DoWork();
   }
   // Analyzer cannot determine which derived class implementation is used
   ```

3. **Virtual Method Overrides**: When a virtual method is overridden, the analyzer can only see the base implementation, not runtime overrides.

   ```cs
   // May not analyze the correct override
   Base instance = new Derived();
   instance.VirtualMethod(); // analyzer sees Base.VirtualMethod, not Derived.VirtualMethod
   ```

4. **External Library Methods**: Methods from external assemblies or NuGet packages where source code is not available cannot be analyzed.

   ```cs
   // Will NOT be analyzed
   externalLibrary.Process(); // source not available
   ```

5. **Reflection-Based Invocations**: Methods invoked via reflection cannot be traced by static analysis.

   ```cs
   // Will NOT be analyzed
   typeof(MyClass).GetMethod("DoWork").Invoke(obj, null);
   ```

6. **Dependency Injection**: When methods are invoked on instances resolved via DI containers, the concrete type is not known at analysis time.

   ```cs
   // Will NOT be analyzed if injected
   public MyOrchestrator(IService service) 
   {
       service.Process(); // analyzer cannot determine concrete type
   }
   ```

##### Diagnostic Messages

When a violation is detected in a helper method, the diagnostic message explicitly indicates:
- The **method** where the violation occurs
- The **specific violation** (e.g., `System.DateTime.Now`)
- The **orchestration** that invokes the method

**Example diagnostic:**
```
"The method 'HelperMethod' uses 'System.DateTime.Now' that may cause non-deterministic behavior when invoked from orchestration 'MyOrchestrator'"
```

This makes it clear that the issue is not with `HelperMethod` itself, but with its invocation from an orchestration context.

##### Consistency Across Analyzers

All orchestration analyzers that extend `OrchestrationAnalyzer<TOrchestrationVisitor>` should use the same method probing behavior by deriving their visitor from `MethodProbeOrchestrationVisitor`. This ensures consistent behavior across all orchestration-related diagnostics.

**Example Implementation:**
```cs
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MyOrchestrationAnalyzer : OrchestrationAnalyzer<MyOrchestrationVisitor>
{
    // Analyzer implementation
    
    public sealed class MyOrchestrationVisitor : MethodProbeOrchestrationVisitor
    {
        protected override void VisitMethod(SemanticModel semanticModel, SyntaxNode methodSyntax, 
            IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            // Inspect the method for violations
            // This will be called for the orchestration entry point and all methods it invokes
        }
    }
}
```

## Unit Testing

There is a [test project](../../test/Analyzers.Tests/) that contains several unit tests for Durable Task analyzers.
We also have some verifier methods that allows you to quickly create the code snippets you would like to test,
such as:

- `CSharpAnalyzerVerifier<TAnalyzer>.VerifyDurableTaskAnalyzerAsync(string source, params DiagnosticResult[] expected)`
- `CSharpCodeFixVerifier<TAnalyzer, TCodeFix>.VerifyDurableTaskAnalyzerAsync(string source, params DiagnosticResult[] expected)`
- `CSharpCodeFixVerifier<TAnalyzer, TCodeFix>.VerifyDurableTaskAnalyzerAsync(string source, params DiagnosticResult[] expected, string fix)`

For instance, if you would like to test `MatchingInputOutputTypeActivityAnalyzer`.
You can start creating a test class and add the following type alias:

```cs
using VerifyCS = Microsoft.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Microsoft.DurableTask.Analyzers.Activities.MatchingInputOutputTypeActivityAnalyzer>;
```

Then, you can use the verifier in your test code:

```cs
[Fact]
public async Task DurableFunctionActivityInvocationWithMismatchedInputType()
{
    string code = @"
<the cs code containing a violation>
";

    // build the expected diagnostic
    DiagnosticResult expected = VerifyCS
                                    .Diagnostic(MatchingInputOutputTypeActivityAnalyzer.InputArgumentTypeMismatchDiagnosticId)
                                    .WithArguments("something");

    // asserts that the diagnostic is reported
    await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
}
```

For additional details on how to test an analyzer, please see [this tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix#build-unit-tests).

## Manual Testing

Any of the [samples projects](../../samples/) in this repository can be used for manual testing and validation.

You can just add a project reference to `..\..\src\Analyzers\Analyzers.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Analyzers\Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
```

> When using Visual Studio, you may need to restart the IDE, since analyzers and code fixers are usually loaded and cached during start up.
Be aware of this behavior when developing new features as well.

### Debugging

Although debugging the unit tests is a great way to inspect an analyzer's internal state,
it can be useful to attach a debugger to one of the samples projects mentioned above.

In order to do that, go to the specific analyzer you want to debug and add the following lines to its `Initialize` method:

```cs
using System.Diagnostics;
...

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class YourAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
    ...
    }
}
```

Then, re-build the solution. You should be able to select an IDE/Debugger and start debugging using breakpoints, etc.

> This method [only works on Windows](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debugger.launch?view=net-8.0).

## Deprecating Analyzers/Code Fixers

From time to time, Roslyn Analyzers can become stale, if the analysis they perform is not valid anymore.
For instance, a given code constraint in Durable Functions may not exist anymore if the runtime is upgraded to support it.

In those cases, we need to deprecate the analyzer or code fixer related to that diagnostic.
The proposed general strategy depends on whether this feature is part of a major or minor release of the Durable Task .NET Client SDK:

- Major Client SDK release: We can just delete the related analyzer from this project and also release a major version of the analyzer.
- Minor Client SDK release: We can suppress the diagnostic using a Roslyn feature called [DiagnosticSuppressor](https://github.com/dotnet/roslyn/blob/main/docs/analyzers/DiagnosticSuppressorDesign.md).

> This `DiagnosticSuppressor` should be added to the Client SDK Release that needs to control which diagnostics must be suppressed.

For instance, this is how we can prevent `DURABLE0001` to be fired:

```cs
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MySupressor : DiagnosticSuppressor
{
    static SuppressionDescriptor Rule = new SuppressionDescriptor("SOMETHING123", "DURABLE0001", "no longer make sense");
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [Rule];

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            context.ReportSuppression(Suppression.Create(Rule, diagnostic));
        }
    }
}
```

For more information on `DiagnosticSuppressor.ReportSuppressions`, please [read the docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.diagnosticsuppressor.reportsuppressions?view=roslyn-dotnet-4.3.0#microsoft-codeanalysis-diagnostics-diagnosticsuppressor-reportsuppressions(microsoft-codeanalysis-diagnostics-suppressionanalysiscontext)).
