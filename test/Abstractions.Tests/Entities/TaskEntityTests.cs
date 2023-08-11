// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using DotNext;

namespace Microsoft.DurableTask.Entities.Tests;

public class TaskEntityTests
{
    [Theory]
    [InlineData("doesNotExist")] // method does not exist.
    [InlineData("add")] // private method, should not work.
    [InlineData("staticMethod")] // public static methods are not supported.
    public async Task OperationNotSupported_Fails(string name)
    {
        Operation operation = new(name, new Context(null), 10);
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
        Operation operation = new(name, new Context(null), sync);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().Be(expected);
    }

    [Theory]
    [CombinatorialData]
    public async Task Add_Success([CombinatorialRange(0, 14)] int method, bool lowercase)
    {
        int start = Random.Shared.Next(0, 10);
        int toAdd = Random.Shared.Next(0, 10);
        string opName = lowercase ? "add" : "Add";
        Context context = new(start);
        Operation operation = new($"{opName}{method}", context, toAdd);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        int expected = start + toAdd;
        context.GetState(typeof(int)).Should().BeOfType<int>().Which.Should().Be(expected);
        result.Should().BeOfType<int>().Which.Should().Be(expected);
    }

    [Theory]
    [CombinatorialData]
    public async Task Get_Success([CombinatorialRange(0, 2)] int method, bool lowercase)
    {
        int expected = Random.Shared.Next(0, 10);
        string opName = lowercase ? "get" : "Get";
        Context context = new(expected);
        Operation operation = new($"{opName}{method}", context, default);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        context.GetState(typeof(int)).Should().BeOfType<int>().Which.Should().Be(expected);
        result.Should().BeOfType<int>().Which.Should().Be(expected);
    }

    [Fact]
    public async Task Add_NoInput_Fails()
    {
        Operation operation = new("add0", new Context(null), default);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [CombinatorialData]
    public async Task Dispatch_AmbiguousArgs_Fails([CombinatorialRange(0, 3)] int method)
    {
        Operation operation = new($"ambiguousArgs{method}", new Context(null), 10);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Dispatch_AmbiguousMatch_Fails()
    {
        Operation operation = new("ambiguousMatch", new Context(null), 10);
        TestEntity entity = new();

        Func<Task<object?>> action = () => entity.RunAsync(operation).AsTask();
        await action.Should().ThrowAsync<AmbiguousMatchException>();
    }

    [Fact]
    public async Task DefaultValue_NoInput_Succeeds()
    {
        Operation operation = new("defaultValue", new Context(null), default);
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeOfType<string>().Which.Should().Be("default");
    }

    [Fact]
    public async Task DefaultValue_Input_Succeeds()
    {
        Operation operation = new("defaultValue", new Context(null), "not-default");
        TestEntity entity = new();

        object? result = await entity.RunAsync(operation);

        result.Should().BeOfType<string>().Which.Should().Be("not-default");
    }

    class Context : TaskEntityContext
    {
        public Context(object? state)
        {
            this.State = state;
        }

        public object? State { get; private set; }

        public override EntityInstanceId Id { get; }

        public override object? GetState(Type type)
        {
            return this.State switch
            {
                null => null,
                _ when type.IsAssignableFrom(this.State.GetType()) => this.State,
                _ => throw new InvalidCastException()
            };
        }

        public override void SetState(object? state)
        {
            this.State = state;
        }

        public override void SignalEntity(
            EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public override void StartOrchestration(
            TaskName name, object? input = null, StartOrchestrationOptions? options = null)
        {
            throw new NotImplementedException();
        }
    }

    class Operation : TaskEntityOperation
    {
        readonly Optional<object?> input;

        public Operation(string name, TaskEntityContext context, Optional<object?> input)
        {
            this.Name = name;
            this.Context = context;
            this.input = input;
        }

        public override string Name { get; }

        public override TaskEntityContext Context { get; }

        public override bool HasInput => this.input.IsPresent;

        public override object? GetInput(Type inputType)
        {
            if (!this.input.IsPresent)
            {
                throw new InvalidOperationException("No input available.");
            }

            if (this.input.Value is null)
            {
                return null;
            }

            if (!inputType.IsAssignableFrom(this.input.Value.GetType()))
            {
                throw new InvalidCastException("Cannot convert input type.");
            }

            return this.input.Value;
        }
    }

    class TestEntity : TaskEntity<int>
    {
        public static string StaticMethod() => throw new NotImplementedException();

        // All possible permutations of the 3 inputs we support: object, context, operation
        // 14 via Add, 2 via Get: 16 total.
        public int Add0(int value) => this.Add(value, default, default);

        public int Add1(int value, TaskEntityContext context) => this.Add(value, context, default);

        public int Add2(int value, TaskEntityOperation operation) => this.Add(value, default, operation);

        public int Add3(int value, TaskEntityContext context, TaskEntityOperation operation)
            => this.Add(value, context, operation);

        public int Add4(int value, TaskEntityOperation operation, TaskEntityContext context)
            => this.Add(value, context, operation);

        public int Add5(TaskEntityOperation operation) => this.Add(default, default, operation);

        public int Add6(TaskEntityOperation operation, int value) => this.Add(value, default, operation);

        public int Add7(TaskEntityOperation operation, TaskEntityContext context)
            => this.Add(default, context, operation);

        public int Add8(TaskEntityOperation operation, int value, TaskEntityContext context)
            => this.Add(value, context, operation);

        public int Add9(TaskEntityOperation operation, TaskEntityContext context, int value)
            => this.Add(value, context, operation);

        public int Add10(TaskEntityContext context, int value)
            => this.Add(value, context, default);

        public int Add11(TaskEntityContext context, TaskEntityOperation operation)
            => this.Add(default, context, operation);

        public int Add12(TaskEntityContext context, int value, TaskEntityOperation operation)
            => this.Add(value, context, operation);

        public int Add13(TaskEntityContext context, TaskEntityOperation operation, int value)
            => this.Add(value, context, operation);

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

        int Add(int? value, Optional<TaskEntityContext> context, Optional<TaskEntityOperation> operation)
        {
            if (context.IsPresent)
            {
                context.Value.Should().NotBeNull();
            }

            if (operation.IsPresent)
            {
                operation.Value.Should().NotBeNull();
            }

            if (!value.HasValue && operation.TryGet(out TaskEntityOperation op))
            {
                value = (int)op.GetInput(typeof(int))!;
            }

            value.HasValue.Should().BeTrue();
            return this.State += value!.Value;
        }

        int Get(Optional<TaskEntityContext> context)
        {
            if (context.IsPresent)
            {
                context.Value.Should().NotBeNull();
            }

            return this.State;
        }
    }
}
