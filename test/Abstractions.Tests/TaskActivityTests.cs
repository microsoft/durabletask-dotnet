// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Tests;

public class TaskActivityTests
{
    [Theory]
    [InlineData(typeof(ReferenceActivity), null)]
    [InlineData(typeof(ReferenceActivity), "")]
    [InlineData(typeof(ReferenceActivity), "input")]
    [InlineData(typeof(NullableReferenceActivity), null)]
    [InlineData(typeof(NullableReferenceActivity), "")]
    [InlineData(typeof(NullableReferenceActivity), "input")]
    [InlineData(typeof(ValueActivity), 0)]
    [InlineData(typeof(ValueActivity), 1)]
    [InlineData(typeof(NullableValueActivity), null)]
    [InlineData(typeof(NullableValueActivity), 0)]
    [InlineData(typeof(NullableValueActivity), 1)]
    public async Task Run_CorrectType_DoesNotThrow(Type t, object? input)
    {
        ITaskActivity activity = (ITaskActivity)Activator.CreateInstance(t)!;
        object? obj = await activity.RunAsync(Mock.Of<TaskActivityContext>(), input);
        obj.Should().Be(input);
    }

    [Theory]
    [InlineData(typeof(ReferenceActivity), 1)]
    [InlineData(typeof(NullableReferenceActivity), 1)]
    [InlineData(typeof(ValueActivity), null)]
    [InlineData(typeof(ValueActivity), "input")]
    [InlineData(typeof(NullableValueActivity), "input")]
    public async Task Run_WrongType_Throws(Type t, object? input)
    {
        ITaskActivity activity = (ITaskActivity)Activator.CreateInstance(t)!;
        Func<Task> act = () => activity.RunAsync(Mock.Of<TaskActivityContext>(), input);
        await act.Should().ThrowExactlyAsync<ArgumentException>();
    }

    class ReferenceActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string input)
        {
            return Task.FromResult(input);
        }
    }

    class NullableReferenceActivity : TaskActivity<string?, string?>
    {
        public override Task<string?> RunAsync(TaskActivityContext context, string? input)
        {
            return Task.FromResult(input);
        }
    }

    class ValueActivity : TaskActivity<int, int>
    {
        public override Task<int> RunAsync(TaskActivityContext context, int input)
        {
            return Task.FromResult(input);
        }
    }

    class NullableValueActivity : TaskActivity<int?, int?>
    {
        public override Task<int?> RunAsync(TaskActivityContext context, int? input)
        {
            return Task.FromResult(input);
        }
    }
}