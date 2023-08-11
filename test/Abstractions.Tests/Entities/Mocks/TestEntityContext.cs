// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Entities.Tests;

public class TestEntityContext : TaskEntityContext
{
    public TestEntityContext(object? state)
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
