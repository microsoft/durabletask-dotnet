# Contributing to DurableTask.Analyzers

This document presents guidelines on how to contribute and develop `DurableTask.Analyzers`,
a series of [Roslyn Analyzers](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview?view=vs-2022)
that inspect Durable Task and Azure Functions code for quality, maintainability, code constraints, and other issues. 

## Requirements

- [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Building

If you are using Visual Studio, you can just open the main [solution file](../../Dapr.DurableTask.sln)
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
using VerifyCS = Dapr.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Dapr.DurableTask.Analyzers.Activities.MatchingInputOutputTypeActivityAnalyzer>;
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
