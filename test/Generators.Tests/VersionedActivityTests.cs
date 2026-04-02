// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Functions.Worker;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.DurableTask.Generators.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Generators.Tests;

public class VersionedActivityTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task Standalone_SingleVersionedActivity_GeneratesUnsuffixedHelper()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceActivity"")]
[DurableTaskVersion(""v1"")]
class InvoiceActivity : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""InvoiceActivity""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""InvoiceActivity"", input, ApplyGeneratedActivityVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    if (options?.Version is { Version: not null and not """" })
    {
        return options;
    }

    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    return new StartOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    if (options is SubOrchestrationOptions { Version: { Version: not null and not """" } })
    {
        return options;
    }

    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return new SubOrchestrationOptions(subOrchestrationOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return new SubOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedActivityVersion(TaskOptions? options, string version)
{
    if (options is ActivityOptions activityOptions
        && activityOptions.Version is TaskVersion explicitVersion
        && !string.IsNullOrWhiteSpace(explicitVersion.Version))
    {
        return options;
    }

    if (options is ActivityOptions existingActivityOptions)
    {
        return new ActivityOptions(existingActivityOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new ActivityOptions
        {
            Version = version,
        };
    }

    return new ActivityOptions(options)
    {
        Version = version,
    };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<InvoiceActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Standalone_MultiVersionedActivities_GenerateVersionQualifiedHelpersOnly()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceActivity"")]
[DurableTaskVersion(""v1"")]
class InvoiceActivityV1 : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceActivity"")]
[DurableTaskVersion(""v2"")]
class InvoiceActivityV2 : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""InvoiceActivityV1""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceActivity_v1Async(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""InvoiceActivity"", input, ApplyGeneratedActivityVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceActivityV2""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceActivity_v2Async(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""InvoiceActivity"", input, ApplyGeneratedActivityVersion(options, ""v2""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    if (options?.Version is { Version: not null and not """" })
    {
        return options;
    }

    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    return new StartOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    if (options is SubOrchestrationOptions { Version: { Version: not null and not """" } })
    {
        return options;
    }

    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return new SubOrchestrationOptions(subOrchestrationOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return new SubOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedActivityVersion(TaskOptions? options, string version)
{
    if (options is ActivityOptions activityOptions
        && activityOptions.Version is TaskVersion explicitVersion
        && !string.IsNullOrWhiteSpace(explicitVersion.Version))
    {
        return options;
    }

    if (options is ActivityOptions existingActivityOptions)
    {
        return new ActivityOptions(existingActivityOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new ActivityOptions
        {
            Version = version,
        };
    }

    return new ActivityOptions(options)
    {
        Version = version,
    };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<InvoiceActivityV1>();
    builder.AddActivity<InvoiceActivityV2>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Standalone_DuplicateLogicalNameAndVersion_ReportsDiagnostic()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceActivity"")]
[DurableTaskVersion(""v1"")]
class InvoiceActivityV1 : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceActivity"")]
[DurableTaskVersion(""v1"")]
class InvoiceActivityV1Duplicate : TaskActivity<int, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Calls the <see cref=""InvoiceActivityV1""/> activity.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallActivityAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""InvoiceActivity"", input, ApplyGeneratedActivityVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    if (options?.Version is { Version: not null and not """" })
    {
        return options;
    }

    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    return new StartOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    if (options is SubOrchestrationOptions { Version: { Version: not null and not """" } })
    {
        return options;
    }

    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return new SubOrchestrationOptions(subOrchestrationOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return new SubOrchestrationOptions(options)
    {
        Version = version,
    };
}

static TaskOptions? ApplyGeneratedActivityVersion(TaskOptions? options, string version)
{
    if (options is ActivityOptions activityOptions
        && activityOptions.Version is TaskVersion explicitVersion
        && !string.IsNullOrWhiteSpace(explicitVersion.Version))
    {
        return options;
    }

    if (options is ActivityOptions existingActivityOptions)
    {
        return new ActivityOptions(existingActivityOptions)
        {
            Version = version,
        };
    }

    if (options is null)
    {
        return new ActivityOptions
        {
            Version = version,
        };
    }

    return new ActivityOptions(options)
    {
        Version = version,
    };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddActivity<InvoiceActivityV1>();
    return builder;
}");

        DiagnosticResult expected = new DiagnosticResult("DURABLE3003", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 12, 14, 12, 31)
            .WithArguments("InvoiceActivity", "v1");

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                GeneratedSources =
                {
                    (typeof(DurableTaskSourceGenerator), GeneratedFileName, SourceText.From(expectedOutput, System.Text.Encoding.UTF8, SourceHashAlgorithm.Sha256)),
                },
                ExpectedDiagnostics = { expected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                },
            },
        };

        return test.RunAsync();
    }

    [Fact]
    public Task AzureFunctions_ClassBasedActivities_DuplicateLogicalNameAcrossVersions_ReportsDiagnostic()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.DependencyInjection;

namespace MyFunctions
{
    [DurableTask(""PaymentActivity"")]
    [DurableTaskVersion(""v1"")]
    class PaymentActivityV1 : TaskActivity<int, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
    }

    [DurableTask(""PaymentActivity"")]
    [DurableTaskVersion(""v2"")]
    class PaymentActivityV2 : TaskActivity<int, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, int input) => Task.FromResult(string.Empty);
    }
}";

        DiagnosticResult firstExpected = new DiagnosticResult("DURABLE3004", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 9, 18, 9, 35)
            .WithArguments("PaymentActivity");
        DiagnosticResult secondExpected = new DiagnosticResult("DURABLE3004", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 16, 18, 16, 35)
            .WithArguments("PaymentActivity");

        CSharpSourceGeneratorVerifier<DurableTaskSourceGenerator>.Test test = new()
        {
            TestState =
            {
                Sources = { code },
                ExpectedDiagnostics = { firstExpected, secondExpected },
                AdditionalReferences =
                {
                    typeof(TaskActivityContext).Assembly,
                    typeof(FunctionAttribute).Assembly,
                    typeof(FunctionContext).Assembly,
                    typeof(ActivityTriggerAttribute).Assembly,
                    typeof(ActivatorUtilities).Assembly,
                },
            },
        };

        return test.RunAsync();
    }
}
