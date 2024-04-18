// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DotNext;

namespace Microsoft.DurableTask.Entities.Tests;

public class TestEntityOperation(string name, TaskEntityState state, Optional<object?> input)
    : TaskEntityOperation
{
    public TestEntityOperation(string name, Optional<object?> input)
        : this(name, new TestEntityState(null), input)
    {
    }

    public TestEntityOperation(string name, object? state, Optional<object?> input)
        : this(name, new TestEntityState(state), input)
    {
    }

    public override string Name { get; } = name;

    public override TaskEntityContext Context { get; } = Mock.Of<TaskEntityContext>();

    public override TaskEntityState State { get; } = state;

    public override bool HasInput => input.HasValue;

    public override object? GetInput(Type inputType)
    {
        if (input.IsUndefined)
        {
            throw new InvalidOperationException("No input available.");
        }

        if (input.IsNull)
        {
            return null;
        }

        if (!inputType.IsAssignableFrom(input.Value!.GetType()))
        {
            throw new InvalidCastException("Cannot convert input type.");
        }

        return input.Value;
    }
}
