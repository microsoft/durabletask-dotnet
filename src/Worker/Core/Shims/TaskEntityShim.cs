// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using DTCore = DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shim that provides the entity context and implements batched execution.
/// </summary>
class TaskEntityShim : DTCore.Entities.TaskEntity
{
    readonly DataConverter dataConverter;
    readonly ITaskEntity taskEntity;
    readonly EntityInstanceId entityId;

    readonly StateShim state;
    readonly ContextShim context;
    readonly OperationShim operation;

    // entities roll back the state and actions when the user code throws an unhandled exception
    string? checkpointedState;
    int checkpointedPosition;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEntityShim"/> class.
    /// </summary>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="taskEntity">The task entity.</param>
    /// <param name="entityId">The entity ID.</param>
    public TaskEntityShim(
        DataConverter dataConverter,
        ITaskEntity taskEntity,
        EntityId entityId)
    {
        this.dataConverter = Check.NotNull(dataConverter);
        this.taskEntity = Check.NotNull(taskEntity);
        this.entityId = new EntityInstanceId(entityId.Name, entityId.Key);
        this.state = new StateShim(this, dataConverter);
        this.context = new ContextShim(this, dataConverter);
        this.operation = new OperationShim(this);
    }

    /// <inheritdoc />
    public override async Task<EntityBatchResult> ExecuteOperationBatchAsync(EntityBatchRequest batch)
    {
        // initialize/reset the state and action list
        this.state.CurrentState = operations.EntityState;
        this.context.Rollback(0);

        List<OperationResult> results = new();

        foreach (OperationRequest current in operations.Operations!)
        {
            OperationRequest currentOperation = operations.Operations![i];
            this.operation.SetNameAndInput(currentOperation.Operation!, currentOperation.Input);
            this.Checkpoint();

            try
            {
                object? result = await this.taskEntity.RunAsync(this.operation);
                string? serializedResult = this.dataConverter.Serialize(result);
                results.Add(new OperationResult() { Result = serializedResult });
            }
            catch (Exception applicationException)
            {
                results.Add(new OperationResult()
                {
                    Result = null,
                    ErrorMessage = "exception in application code",
                    FailureDetails = new FailureDetails(applicationException),
                });

                this.Rollback();
            }
        }

        this.ClearCheckpoint(); // want to ensure the old state can be GC'd even if this shim is being cached

        return new EntityBatchResult()
        {
            Results = results,
            Actions = this.context.Actions.ToList(), // make copy to avoid concurrent modification if this shim is reused
            EntityState = this.state.CurrentState,
        };
    }

    void Checkpoint()
    {
        this.checkpointedPosition = this.context.CurrentPosition;
        this.checkpointedState = this.state.CurrentState;
    }

    void ClearCheckpoint()
    {
        this.checkpointedPosition = 0;
        this.checkpointedState = null;
    }

    void Rollback()
    {
        this.state.CurrentState = this.checkpointedState;
        this.context.Rollback(this.checkpointedPosition);
    }

    class StateShim : TaskEntityState
    {
        readonly TaskEntityShim taskEntityShim;
        readonly DataConverter dataConverter;

        string? value;
        object? cachedValue;
        bool cacheValid;

        public StateShim(TaskEntityShim taskEntityShim, DataConverter dataConverter)
        {
            this.taskEntityShim = taskEntityShim;
            this.dataConverter = dataConverter;
        }

        public string? CurrentState
        {
            get => this.value;
            set
            {
                if (this.value != value)
                {
                    this.value = value;
                    this.cachedValue = null;
                    this.cacheValid = false;
                }
            }
        }

        public override object? GetState(Type type)
        {
            if (!this.cacheValid)
            {
                this.cachedValue = this.dataConverter.Deserialize(this.value, type);
                this.cacheValid = true;
            }

            return this.cachedValue;
        }

        public override void SetState(object? state)
        {
            this.value = this.dataConverter.Serialize(state);
            this.cachedValue = state;
            this.cacheValid = true;
        }
    }

    class ContextShim : TaskEntityContext
    {
        readonly TaskEntityShim taskEntityShim;
        readonly DataConverter dataConverter;
        readonly List<OperationAction> operationActions;

        public ContextShim(TaskEntityShim taskEntityShim, DataConverter dataConverter)
        {
            this.taskEntityShim = taskEntityShim;
            this.dataConverter = dataConverter;
            this.operationActions = new List<OperationAction>();
        }

        public IEnumerable<OperationAction> Actions => this.operationActions ?? Enumerable.Empty<OperationAction>();

        public int CurrentPosition => this.operationActions?.Count ?? 0;

        public override EntityInstanceId Id => this.taskEntityShim.entityId!;

        public void Rollback(int position)
        {
            if (position < this.operationActions.Count)
            {
                this.operationActions.RemoveRange(position, this.operationActions.Count - position);
            }
        }

        public override void SignalEntity(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
        {
            this.operationActions.Add(new SendSignalOperationAction()
            {
                InstanceId = id.ToString(),
                Name = operationName,
                Input = this.dataConverter.Serialize(input),
                ScheduledTime = options?.SignalTime?.UtcDateTime,
            });
        }

        public override void StartOrchestration(TaskName name, object? input = null, StartOrchestrationOptions? options = null)
        {
            this.operationActions.Add(new StartNewOrchestrationOperationAction()
            {
                Name = name.Name,
                Version = name.Version,
                InstanceId = Guid.NewGuid().ToString("N"),
                Input = this.dataConverter.Serialize(input),
            });
        }
    }

    class OperationShim : TaskEntityOperation
    {
        readonly TaskEntityShim taskEntityShim;

        string? name;
        string? input;

        public OperationShim(TaskEntityShim taskEntityShim)
        {
            this.taskEntityShim = taskEntityShim;
        }

        public override string Name => this.name!; // name is always set before user code can access this property

        public override TaskEntityContext Context => this.taskEntityShim.context;

        public override TaskEntityState State => this.taskEntityShim.state;

        public override bool HasInput => this.input != null;

        public override object? GetInput(Type inputType)
        {
            return this.taskEntityShim.dataConverter.Deserialize(this.input, inputType);
        }

        public void SetNameAndInput(string name, string? input)
        {
            this.name = name;
            this.input = input;
        }
    }
}
