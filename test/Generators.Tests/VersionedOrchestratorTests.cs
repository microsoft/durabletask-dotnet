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

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflow""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
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
    public Task Standalone_MultiVersionedOrchestrator_GeneratesVersionParameterHelpers()
    {
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(""InvoiceWorkflow"", Version = ""v1,v2"")]
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
/// <remarks>Stamps the supplied <paramref name=""version""/> on the started instance; it must be one of the declared versions: v1, v2. A non-null <paramref name=""options""/>.Version overrides it.</remarks>
/// <param name=""version"">The declared task version to stamp on the call. Must be one of: v1, v2.</param>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, string version, StartOrchestrationOptions? options = null)
{
    if (!(version == ""v1"" || version == ""v2""))
    {
        throw new ArgumentException(
            ""Version '"" + version + ""' is not a declared version of orchestration 'InvoiceWorkflow'. Declared versions: v1, v2."",
            nameof(version));
    }

    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, version));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflow""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps the supplied <paramref name=""version""/> on the sub-orchestration; it must be one of the declared versions: v1, v2. A non-null <paramref name=""options""/>.Version overrides it.</remarks>
/// <param name=""version"">The declared task version to stamp on the call. Must be one of: v1, v2.</param>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, string version, TaskOptions? options = null)
{
    if (!(version == ""v1"" || version == ""v2""))
    {
        throw new ArgumentException(
            ""Version '"" + version + ""' is not a declared version of orchestration 'InvoiceWorkflow'. Declared versions: v1, v2."",
            nameof(version));
    }

    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, version));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
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
    public Task Standalone_VersionedOrchestratorWithImplicitName_UsesClassNameNotVersion()
    {
        // Pins the bug fix: previously [DurableTask(Version = "v1")] (no positional task name) caused the
        // generator to read "v1" as the task name. The first argument here is a named property arg, so
        // the generator must fall back to the class name and only consume the Version named arg.
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(Version = ""v1"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflow""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
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
    public Task Standalone_VersionedOrchestratorWithNamedCtorNameArg_UsesNamedArgAsTaskName()
    {
        // Pins T1 fix: [DurableTask(name: "X", Version = "v1")] must read "X" as the task name.
        // Prior to the fix, requiring NameColon is null skipped the named ctor arg and fell back
        // to the class name. Now we also accept named ctor args targeting the "name" parameter.
        string code = @"
using System.Threading.Tasks;
using Microsoft.DurableTask;

[DurableTask(name: ""InvoiceWorkflow"", Version = ""v1"")]
class InvoiceWorkflowImpl : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(
            GeneratedClassName,
            methodList: @"
/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowImpl""/> orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowImpl""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowImpl>();
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

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceWorkflow"", Version = ""v2"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowV1InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowV1Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV2""/> orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v2</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowV2InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV2""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v2</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowV2Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
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

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""invoiceworkflow"", Version = ""v2"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowV1InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowV1Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Schedules a new instance of the <see cref=""InvoiceWorkflowV2""/> orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v2</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowV2InstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""invoiceworkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV2""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v2</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowV2Async(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""invoiceworkflow"", input, ApplyGeneratedVersion(options, ""v2""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
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

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    return builder;
}");

        DiagnosticResult expected = new DiagnosticResult("DURABLE3003", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 11, 14, 11, 31)
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

[DurableTask(""InvoiceWorkflow"", Version = ""v1"")]
class InvoiceWorkflowV1 : TaskOrchestrator<int, string>
{
    public override Task<string> RunAsync(TaskOrchestrationContext context, int input) => Task.FromResult(string.Empty);
}

[DurableTask(""invoiceworkflow"", Version = ""V1"")]
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
/// <remarks>Stamps version <c>v1</c> on the started instance. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""IOrchestrationSubmitter.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewInvoiceWorkflowInstanceAsync(
    this IOrchestrationSubmitter client, int input, StartOrchestrationOptions? options = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

/// <summary>
/// Calls the <see cref=""InvoiceWorkflowV1""/> sub-orchestrator.
/// </summary>
/// <remarks>Stamps version <c>v1</c> on the sub-orchestration. A non-null <paramref name=""options""/>.Version overrides this baked version.</remarks>
/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync(TaskName, object?, TaskOptions?)""/>
public static Task<string> CallInvoiceWorkflowAsync(
    this TaskOrchestrationContext context, int input, TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(""InvoiceWorkflow"", input, ApplyGeneratedVersion(options, ""v1""));
}

static StartOrchestrationOptions? ApplyGeneratedVersion(StartOrchestrationOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is null)
    {
        return new StartOrchestrationOptions
        {
            Version = version,
        };
    }

    if (options.Version is not null)
    {
        return options;
    }

    return options with { Version = new TaskVersion(version) };
}

static TaskOptions? ApplyGeneratedVersion(TaskOptions? options, string version)
{
    // Caller-supplied options.Version is preserved as-is — the explicit value wins. Otherwise we
    // stamp the version that the generated helper was emitted for.
    if (options is SubOrchestrationOptions subOrchestrationOptions)
    {
        return subOrchestrationOptions.Version is not null
            ? subOrchestrationOptions
            : subOrchestrationOptions with { Version = new TaskVersion(version) };
    }

    if (options is null)
    {
        return new SubOrchestrationOptions
        {
            Version = version,
        };
    }

    return options.Version is not null
        ? options
        : new SubOrchestrationOptions(options) { Version = version };
}

internal static DurableTaskRegistry AddAllGeneratedTasks(this DurableTaskRegistry builder)
{
    builder.AddOrchestrator<InvoiceWorkflowV1>();
    return builder;
}");

        DiagnosticResult expected = new DiagnosticResult("DURABLE3003", DiagnosticSeverity.Error)
            .WithSpan("/0/Test0.cs", 11, 14, 11, 31)
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
