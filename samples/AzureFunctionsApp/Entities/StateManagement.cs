// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace AzureFunctionsApp.Entities;

public class StateManagement : TaskEntity<MyState>
{
    readonly ILogger logger;

    public StateManagement(ILogger<StateManagement> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Optional property to override. When 'true', this will allow dispatching of operations to the TState object if
    /// there is no matching method on the entity. Default is 'false'.
    /// </summary>
    protected override bool AllowStateDispatch => base.AllowStateDispatch;

    public MyState Get() => this.State;

    public void CustomDelete()
    {
        // Deleting an entity is done by null-ing out the state.
        // The '!' in `null!;` is only needed because we are using C# explicit nullability.
        // This can be avoided by either:
        // 1) Declare TaskEntity<MyState?> instead.
        // 2) Disable explicit nullability.
        this.State = null!;
    }

    public void Delete()
    {
        // Entities have an implicit 'delete' operation when there is no matching 'delete' method. By explicitly adding
        // a 'Delete' method, it will override the implicit 'delete' operation.
        // Since state deletion is determined by nulling out this.State, it means that value-types cannot be deleted
        // except by the implicit delete (this will still delete it). To delete a value-type, you can declare it as
        // nullable such as TaskEntity<int?> instead of TaskEntity<int>.
        this.State = null!;
    }

    protected override MyState InitializeState(TaskEntityOperation operation)
    {
        // This method allows for customizing the default state value for a new entity.
        return new("Default", 10);
    }
}


public record MyState(string PropA, int PropB);
