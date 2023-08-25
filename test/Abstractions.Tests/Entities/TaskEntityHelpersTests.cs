// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities.Tests;

public class TaskEntityHelpersTests
{
    [Fact]
    public async Task UnwrapAsync_Void()
    {
        int state = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        object? result = await TaskEntityHelpers.UnwrapAsync(context, () => state, null, typeof(void));

        result.Should().BeNull();
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Fact]
    public async Task UnwrapAsync_Object()
    {
        int state = Random.Shared.Next(1, 10);
        int value = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        object? result = await TaskEntityHelpers.UnwrapAsync(context, () => state, value, typeof(int));

        result.Should().BeOfType<int>().Which.Should().Be(value);
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_Task(bool async)
    {
        TaskCompletionSource tcs = new();
        int state = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.TrySetResult();
        }

        ValueTask<object?> task = TaskEntityHelpers.UnwrapAsync(context, () => state, tcs.Task, typeof(Task));

        if (async)
        {
            state++; // Make sure state changes are captured
            tcs.TrySetResult();
        }

        object? result = await task;

        result.Should().BeNull();
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_Task_Throws(bool async)
    {
        TaskCompletionSource tcs = new();
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        Func<Task> throws = async () => await TaskEntityHelpers.UnwrapAsync(context, () => 0, tcs.Task, typeof(Task));
        
        if (async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        await throws.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_TaskOfInt(bool async)
    {
        TaskCompletionSource<int> tcs = new();

        int state = Random.Shared.Next(1, 10);
        int value = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.TrySetResult(value);
        }

        ValueTask<object?> task = TaskEntityHelpers.UnwrapAsync(context, () => state, tcs.Task, typeof(Task<int>));

        if (async)
        {
            state++; // Make sure state changes are captured
            tcs.TrySetResult(value);
        }

        object? result = await task;

        result.Should().BeOfType<int>().Which.Should().Be(value);
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_TaskOfInt_Throws(bool async)
    {
        TaskCompletionSource<int> tcs = new();
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        Func<Task> throws = async () => await TaskEntityHelpers.UnwrapAsync(
            context, () => 0, tcs.Task, typeof(Task<int>));

        if (async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        await throws.Should().ThrowExactlyAsync<OperationCanceledException>();
    }


    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_ValueTask(bool async)
    {
        TaskCompletionSource tcs = new();

        int state = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.TrySetResult();
        }

        ValueTask<object?> task = TaskEntityHelpers.UnwrapAsync(
            context, () => state, new ValueTask(tcs.Task), typeof(ValueTask));

        if (async)
        {
            state++; // Make sure state changes are captured
            tcs.TrySetResult();
        }

        object? result = await task;
        result.Should().BeNull();
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_ValueTask_Throws(bool async)
    {
        TaskCompletionSource tcs = new();
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        Func<Task> throws = async () => await TaskEntityHelpers.UnwrapAsync(
            context, () => 0, new ValueTask(tcs.Task), typeof(ValueTask));

        if (async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        await throws.Should().ThrowExactlyAsync<OperationCanceledException>();
    }


    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_ValueTaskOfInt(bool async)
    {
        TaskCompletionSource<int> tcs = new();
        int state = Random.Shared.Next(1, 10);
        int value = Random.Shared.Next(1, 10);
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.TrySetResult(value);
        }

        ValueTask<object?> task = TaskEntityHelpers.UnwrapAsync(
            context, () => state, new ValueTask<int>(tcs.Task), typeof(ValueTask<int>));

        if (async)
        {
            state++; // Make sure state changes are captured
            tcs.TrySetResult(value);
        }

        object? result = await task;

        result.Should().BeOfType<int>().Which.Should().Be(value);
        context.State.Should().BeOfType<int>().Which.Should().Be(state);
    }

    [Theory]
    [CombinatorialData]
    public async Task UnwrapAsync_ValueTaskOfInt_Throws(bool async)
    {
        TaskCompletionSource<int> tcs = new();
        TestEntityContext context = new(null);

        if (!async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        Func<Task> throws = async () => await TaskEntityHelpers.UnwrapAsync(
            context, () => 0, new ValueTask<int>(tcs.Task), typeof(ValueTask<int>));

        if (async)
        {
            tcs.SetException(new OperationCanceledException());
        }

        await throws.Should().ThrowExactlyAsync<OperationCanceledException>();
    }
}
