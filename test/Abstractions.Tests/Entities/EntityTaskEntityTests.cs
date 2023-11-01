// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using DotNext;
using FluentAssertions.Specialized;

namespace Microsoft.DurableTask.Entities.Tests;

public class EntityTaskEntityTests
{
    [Theory]
    [InlineData("doesNotExist")] // method does not exist.
    [InlineData("add")] // private method, should not work.
    [InlineData("staticMethod")] // public static methods are not supported.
    public async Task OperationNotSupported_Fails(string name)
    {
        TestEntityOperation operation = new(name, 10);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();

        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Theory]
    [CombinatorialData]
    public async Task TaskOperation_Success(
        [CombinatorialValues("TaskOp", "TaskOfStringOp", "ValueTaskOp", "ValueTaskOfStringOp")] string name, bool sync)
    {
        object? expected = name.Contains("OfString") ? "success" : null;
        TestEntityOperation operation = new(name, sync);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().Be(expected);
    }

    [Theory]
    [CombinatorialData]
    public async Task Add_Success([CombinatorialRange(0, 2)] int method, bool lowercase)
    {
        int start = Random.Shared.Next(0, 10);
        int toAdd = Random.Shared.Next(1, 10);
        string opName = lowercase ? "add" : "Add";
        TestEntityState state = new(start);
        TestEntityOperation operation = new($"{opName}{method}", state, toAdd);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        int expected = start + toAdd;
        state.GetState(typeof(int)).Should().BeOfType<int>().Which.Should().Be(expected);
        result.Should().BeOfType<int>().Which.Should().Be(expected);
    }

    [Theory]
    [CombinatorialData]
    public async Task Get_Success([CombinatorialRange(0, 2)] int method, bool lowercase)
    {
        int expected = Random.Shared.Next(0, 10);
        string opName = lowercase ? "get" : "Get";
        TestEntityState state = new(expected);
        TestEntityOperation operation = new($"{opName}{method}", state, default);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        state.GetState(typeof(int)).Should().BeOfType<int>().Which.Should().Be(expected);
        result.Should().BeOfType<int>().Which.Should().Be(expected);
    }

    [Fact]
    public async Task Add_NoInput_Fails()
    {
        TestEntityOperation operation = new("add0", new TestEntityState(null), default);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [CombinatorialData]
    public async Task Dispatch_AmbiguousArgs_Fails([CombinatorialRange(0, 3)] int method)
    {
        TestEntityOperation operation = new($"ambiguousArgs{method}", new TestEntityState(null), 10);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Dispatch_AmbiguousMatch_Fails()
    {
        TestEntityOperation operation = new("ambiguousMatch", new TestEntityState(null), 10);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();
        await action.Should().ThrowAsync<AmbiguousMatchException>();
    }

    [Fact]
    public async Task DefaultValue_NoInput_Succeeds()
    {
        TestEntityOperation operation = new("defaultValue", new TestEntityState(null), default);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeOfType<string>().Which.Should().Be("default");
    }

    [Fact]
    public async Task DefaultValue_Input_Succeeds()
    {
        TestEntityOperation operation = new("defaultValue", new TestEntityState(null), "not-default");
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeOfType<string>().Which.Should().Be("not-default");
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("Delete")]
    public async Task ImplicitDelete_ClearsState(string op)
    {
        TestEntityOperation operation = new(op, 10, default);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeNull();
        operation.State.GetState(typeof(object)).Should().BeNull();
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("Delete")]
    public async Task ExplicitDelete_Overridden(string op)
    {
        TestEntityOperation operation = new(op, 10, default);
        DeleteEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeNull();
        operation.State.GetState(typeof(int)).Should().Be(0);
    }

    [Fact]
    public async Task Throws_ExceptionPreserved()
    {
        string error = "this message should be preserved";
        TestEntityOperation operation = new("throws", error);
        TestEntity entity = new();

        Func<Task> act = async () => await entity.RunAsync(operation);

        ExceptionAssertions<InvalidOperationException> ex = await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(error)
            .Where(x => x.StackTrace!.StartsWith("   at Microsoft.DurableTask.Entities.Tests.EntityTaskEntityTests.TestEntity.Throws(String message)"));
    }

#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0060 // Remove unused parameter
    class TestEntity : TaskEntity<int>
    {
        public static string StaticMethod() => throw new NotImplementedException();

        public int Add0(int value) => this.Add(value, default);

        public int Add1(int value, TaskEntityContext context) => this.Add(value, context);

        public int Get0() => this.Get(default);

        public int Get1(TaskEntityContext context) => this.Get(context);

        public int AmbiguousMatch(TaskEntityContext context) => this.State;

        public int AmbiguousMatch(TaskEntityOperation operation) => this.State;

        public int AmbiguousArgs0(int value, object other) => this.Add0(value);

        public int AmbiguousArgs1(int value, TaskEntityContext context, TaskEntityContext context2) => this.Add0(value);

        public int AmbiguousArgs2(int value, TaskEntityOperation operation, TaskEntityOperation operation2)
            => this.Add0(value);

        public string DefaultValue(string toReturn = "default") => toReturn;

        public Task TaskOp(bool sync)
        {
            static async Task Slow()
            {
                await Task.Yield();
            }

            return sync ? Task.CompletedTask : Slow();
        }

        public Task<string> TaskOfStringOp(bool sync)
        {
            static async Task<string> Slow()
            {
                await Task.Yield();
                return "success";
            }

            return sync ? Task.FromResult("success") : Slow();
        }

        public ValueTask ValueTaskOp(bool sync)
        {
            static async Task Slow()
            {
                await Task.Yield();
            }

            return sync ? default : new(Slow());
        }

        public ValueTask<string> ValueTaskOfStringOp(bool sync)
        {
            static async Task<string> Slow()
            {
                await Task.Yield();
                return "success";
            }

            return sync ? new("success") : new(Slow());
        }

        public void Throws(string message)
        {
            throw new InvalidOperationException(message);
        }

        int Add(int? value, Optional<TaskEntityContext> context)
        {
            if (context.HasValue)
            {
                context.Value.Should().NotBeNull();
            }

            value.HasValue.Should().BeTrue();
            return this.State += value!.Value;
        }

        int Get(Optional<TaskEntityContext> context)
        {
            if (context.HasValue)
            {
                context.Value.Should().NotBeNull();
            }

            return this.State;
        }
    }

    class DeleteEntity : TaskEntity<int>
    {
        public void Delete() => this.State = 0;
    }
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore CA1822 // Mark members as static
}
