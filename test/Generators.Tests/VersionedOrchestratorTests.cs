// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.DurableTask.Generators.Tests.Utils;

namespace Microsoft.DurableTask.Generators.Tests;

public class VersionedOrchestratorTests
{
    const string GeneratedClassName = "GeneratedDurableTaskExtensions";
    const string GeneratedFileName = $"{GeneratedClassName}.cs";

    [Fact]
    public Task Standalone_SingleVersionedOrchestrator_GeneratesVersionAwareHelpers()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflow : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflow""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflow""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflow>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Standalone_MultiVersionedOrchestrators_GenerateVersionQualifiedHelpersOnly()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v2"")]
class InvoiceWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV1""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflow_v1InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflow_v1Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV2""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflow_v2InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV2""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflow_v2Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v2""));
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    builder.AddOrchestrator<InvoiceWorkflowV2>();
    return builder;
}");

        return TestHelpers.RunTestAsync<DurableTaskSourceGenerator>(
            GeneratedFileName,
            code,
            expectedOutput,
            isDurableFunctions: false);
    }

    [Fact]
    public Task Standalone_CaseInsensitiveLogicalNameGrouping_GeneratesVersionQualifiedHelpersOnly()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""invoiceworkflow"")]
[DurableTaskVersion(""v2"")]
class InvoiceWorkflowV2 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV1""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflow_v1InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflow_v1Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV2""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewinvoiceworkflow_v2InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""invoiceworkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV2""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> Callinvoiceworkflow_v2Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""invoiceworkflow"", input, ApplyGeneratedVersion(options, ""v2""));
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    builder.AddOrchestrator<InvoiceWorkflowV2>();
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

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflowV1Duplicate : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV1""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    return builder;
}");

        DiagnosticResult expected = new DiagnosticResult("DURABLE3003", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 12, 14, 12, 31)
            .WithArguments("InvoiceWorkflow", "v1");

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
    public Task Standalone_DuplicateLogicalNameAndVersion_DifferingOnlyByCase_ReportsDiagnostic()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceWorkflow"")]
[DurableTaskVersion(""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""invoiceworkflow"")]
[DurableTaskVersion(""V1"")]
class InvoiceWorkflowV1Duplicate : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV1""/> orchestrator.
/// </summary>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
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

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    return builder;
}");

        DiagnosticResult expected = new DiagnosticResult("DURABLE3003", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 12, 14, 12, 31)
            .WithArguments("invoiceworkflow", "V1");

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
}
