// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Tests;

public class TaskOrchestratorTests
{
    [Theory]
    [InlineData(typeof(ReferenceOrchestrator), null)]
    [InlineData(typeof(ReferenceOrchestrator), "input")]
    [InlineData(typeof(NullableReferenceOrchestrator), null)]
    [InlineData(typeof(NullableReferenceOrchestrator), "input")]
    [InlineData(typeof(ValueOrchestrator), null)]
    [InlineData(typeof(ValueOrchestrator), 1)]
    [InlineData(typeof(NullableValueOrchestrator), null)]
    [InlineData(typeof(NullableValueOrchestrator), 1)]
    public async Task Run_CorrectType_DoesNotThrow(Type t, object? input)
    {
        ITaskOrchestrator orchestrator = (ITaskOrchestrator)Activator.CreateInstance(t)!;
        object? obj = await orchestrator.RunAsync(Mock.Of<TaskOrchestrationContext>(), input);
        object? expected = t == typeof(ValueOrchestrator) && input is null ? 0 : input; // need to special case the value type.
        obj.Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(ReferenceOrchestrator), 1)]
    [InlineData(typeof(NullableReferenceOrchestrator), 1)]
    [InlineData(typeof(ValueOrchestrator), "input")]
    [InlineData(typeof(NullableValueOrchestrator), "input")]
    public async Task Run_WrongType_Throws(Type t, object? input)
    {
        ITaskOrchestrator orchestrator = (ITaskOrchestrator)Activator.CreateInstance(t)!;
        Func<Task> act = () => orchestrator.RunAsync(Mock.Of<TaskOrchestrationContext>(), input);
        await act.Should().ThrowExactlyAsync<ArgumentException>();
    }

    class ReferenceOrchestrator : TaskOrchestrator<string, string>
    {
        public override Task<string> RunAsync(TaskOrchestrationContext context, string input)
        {
            return Task.FromResult(input);
        }
    }

    class NullableReferenceOrchestrator : TaskOrchestrator<string?, string?>
    {
        public override Task<string?> RunAsync(TaskOrchestrationContext context, string? input)
        {
            return Task.FromResult(input);
        }
    }

    class ValueOrchestrator : TaskOrchestrator<int, int>
    {
        public override Task<int> RunAsync(TaskOrchestrationContext context, int input)
        {
            return Task.FromResult(input);
        }
    }

    class NullableValueOrchestrator : TaskOrchestrator<int?, int?>
    {
        public override Task<int?> RunAsync(TaskOrchestrationContext context, int? input)
        {
            return Task.FromResult(input);
        }
    }
}