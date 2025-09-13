// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DotNext;

namespace Microsoft.DurableTask.Entities.Tests;

public class TestEntityOperation : TaskEntityOperation
{
    readonly Optional<object?> input;

    public TestEntityOperation(string name, Optional<object?> input)
        : this(name, new TestEntityState(null), input)
    {
    }

    public TestEntityOperation(string name, object? state, Optional<object?> input)
        : this(name, new TestEntityState(state), input)
    {
    }

    public TestEntityOperation(string name, TaskEntityState state, Optional<object?> input)
    {
        this.Name = name;
        this.State = state;
        this.input = input;
        this.Context = Mock.Of<TaskEntityContext>();
    }

    public override string Name { get; }

    public override TaskEntityContext Context { get; }

    public override TaskEntityState State { get; }

    public override bool HasInput => this.input.HasValue;

    public override object? GetInput(Type inputType)
    {
        if (this.input.IsUndefined)
        {
            throw new InvalidOperationException("No input available.");
        }

        if (this.input.IsNull)
        {
            return null;
        }

        if (!inputType.IsAssignableFrom(this.input.Value!.GetType()))
        {
            throw new InvalidCastException("Cannot convert input type.");
        }

        return this.input.Value;
    }

    public override Task<object?> GetInputAsync(Type inputType)
    {
        return Task.FromResult(this.GetInput(inputType));
    }
}
