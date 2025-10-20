// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Generators.Tests;

static class SourceHelpers
{
    public static string OrchestrationGeneric(
        string className,
        string? taskName = null,
        string inputType = "string",
        string outputType = "string",
        string result = "string.Empty",
        bool attribute = true)
    {
        string attr = attribute ? $"[DurableTask(\"{taskName ?? className}\")]" : string.Empty;
        return $$"""
        using System.Threading.Tasks;

        namespace Microsoft.DurableTask.Generators.Tests;

        {{attr}}
        public class {{className}} : TaskOrchestrator<{{inputType}}, {{outputType}}>
        {
            public override Task<{{outputType}}> RunAsync(TaskOrchestrationContext context, {{inputType}} input)
            {
                return Task.FromResult({{result}});
            }
        }
        """;
    }

    public static string OrchestrationInterface(
        string className,
        string? taskName = null,
        string inputType = "string",
        string outputType = "string",
        string result = "string.Empty")
    {
        taskName ??= className;
        return $$"""
        using System.Threading.Tasks;

        namespace Microsoft.DurableTask.Generators.Tests;

        [DurableTask("{{taskName}}")]
        public class {{className}} : ITaskOrchestrator
        {
            Type ITaskOrchestrator.InputType => typeof({{inputType}});
            Type ITaskOrchestrator.OutputType => typeof({{outputType}});

            Task<object?> ITaskOrchestrator.RunAsync(TaskOrchestrationContext context, object? input)
            {
                return Task.FromResult<object?>({{result}});
            }
        }
        """;
    }

    public static string ActivityGeneric(
        string className,
        string? taskName = null,
        string inputType = "string",
        string outputType = "string",
        string result = "string.Empty")
    {
        taskName ??= className;
        return $$"""
        using System.Threading.Tasks;
        
        namespace Microsoft.DurableTask.Generators.Tests;

        [DurableTask("{{taskName}}")]
        public class {{className}} : TaskActivity<{{inputType}}, {{outputType}}>
        {
            public override Task<{{outputType}}> RunAsync(TaskActivityContext context, {{inputType}} input)
            {
                return Task.FromResult({{result}});
            }
        }
        """;
    }

    public static string ActivityInterface(
        string className,
        string? taskName = null,
        string inputType = "string",
        string outputType = "string",
        string result = "string.Empty")
    {
        taskName ??= className;
        return $$"""
        using System.Threading.Tasks;
        
        namespace Microsoft.DurableTask.Generators.Tests;

        [DurableTask("{{taskName}}")]
        public class {{className}} : ITaskActivity
        {
            Type ITaskActivity.InputType => typeof({{inputType}});
            Type ITaskActivity.OutputType => typeof({{outputType}});

            Task<object?> ITaskActivity.RunAsync(TaskActivityContext context, object? input)
            {
                return Task.FromResult<object?>({{result}});
            }
        }
        """;
    }

    public static string EntityGeneric(
        string className,
        string? taskName = null,
        string stateType = "object")
    {
        taskName ??= className;
        return $$"""
        using System.Threading.Tasks;
        using Microsoft.DurableTask.Entities;
        
        namespace Microsoft.DurableTask.Generators.Tests;

        [DurableTask("{{taskName}}")]
        public class {{className}} : TaskEntity<{{stateType}}>
        {
        }
        """;
    }

    public static string EntityInterface(
        string className,
        string? taskName = null)
    {
        taskName ??= className;
        return $$"""
        using System.Threading.Tasks;
        using Microsoft.DurableTask.Entities;
        
        namespace Microsoft.DurableTask.Generators.Tests;

        [DurableTask("{{taskName}}")]
        public class {{className}} : ITaskEntity
        {
            ValueTask<object?> ITaskEntity.RunAsync(TaskEntityOperation operation)
            {
                return default;
            }
        }
        """;
    }
}