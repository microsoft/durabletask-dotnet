﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities.Tests;

public class TestEntityState : TaskEntityState
{
    public TestEntityState(object? state)
    {
        this.State = state;
    }

    public override bool HasState => this.State != null;

    public object? State { get; private set; }

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
}
