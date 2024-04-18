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
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskEntityShim"/> class.
    /// </summary>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="taskEntity">The task entity.</param>
    /// <param name="entityId">The entity ID.</param>
    /// <param name="logger">The logger.</param>
    public TaskEntityShim(
        DataConverter dataConverter, ITaskEntity taskEntity, EntityId entityId, ILogger logger)
    {
        this.dataConverter = Check.NotNull(dataConverter);
        this.taskEntity = Check.NotNull(taskEntity);
        this.entityId = new EntityInstanceId(entityId.Name, entityId.Key);
        this.state = new StateShim(dataConverter);
        this.context = new ContextShim(this.entityId, dataConverter);
        this.operation = new OperationShim(this);
        this.logger = logger;
    }

    /// <inheritdoc />
    public override async Task<EntityBatchResult> ExecuteOperationBatchAsync(EntityBatchRequest operations)
    {
        // set the current state, and commit it so we can roll back to it later.
        // The commit/rollback mechanism is needed since we treat entity operations transactionally.
        // This means that if an operation throws an unhandled exception, all its effects are rolled back.
        // In particular, (1) the entity state is reverted to what it was prior to the operation, and
        // (2) all of the messages sent by the operation (e.g. if it started a new orchestrations) are discarded.
        this.state.CurrentState = operations.EntityState;
        this.state.Commit();

        List<OperationResult> results = new();

        foreach (OperationRequest current in operations.Operations!)
        {
            this.operation.SetNameAndInput(current.Operation!, current.Input);

            try
            {
                object? result = await this.taskEntity.RunAsync(this.operation);
                string? serializedResult = this.dataConverter.Serialize(result);
                results.Add(new OperationResult() { Result = serializedResult });

                // the user code completed without exception, so we commit the current state and actions.
                this.state.Commit();
                this.context.Commit();
            }
            catch (Exception applicationException)
            {
                this.logger.OperationError(applicationException, this.entityId, current.Operation!);
                results.Add(new OperationResult()
                {
                    FailureDetails = new FailureDetails(applicationException),
                });

                // the user code threw an unhandled exception, so we roll back the state and the actions.
                this.state.Rollback();
                this.context.Rollback();
            }
        }

        var batchResult = new EntityBatchResult()
        {
            Results = results,
            Actions = this.context.Actions,
            EntityState = this.state.CurrentState,
            FailureDetails = null,
        };

        // we reset only the context, but keep the current state.
        // this makes it possible to reuse the cached state object if the TaskEntityShim is reused.
        this.context.Reset();

        return batchResult;
    }

    class StateShim(DataConverter dataConverter) : TaskEntityState
    {
        string? value;
        object? cachedValue;
        string? checkpointValue;

        /// <inheritdoc />
        public override bool HasState => this.value != null;

        public string? CurrentState
        {
            get => this.value;
            set
            {
                if (this.value != value)
                {
                    this.value = value;
                    this.cachedValue = null;
                }
            }
        }

        public void Commit()
        {
            this.checkpointValue = this.value;
        }

        public void Rollback()
        {
            this.CurrentState = this.checkpointValue;
            this.cachedValue = null;
        }

        public void Reset()
        {
            this.CurrentState = default;
            this.cachedValue = null;
        }

        public override object? GetState(Type type)
        {
            if (this.cachedValue?.GetType() is Type t && t.IsAssignableFrom(type))
            {
                return this.cachedValue;
            }

            this.cachedValue = dataConverter.Deserialize(this.value, type);
            return this.cachedValue;
        }

        public override void SetState(object? state)
        {
            this.value = dataConverter.Serialize(state);
            this.cachedValue = state;
        }
    }

    class ContextShim(EntityInstanceId entityInstanceId, DataConverter dataConverter) : TaskEntityContext
    {
        List<OperationAction> operationActions = new List<OperationAction>();
        int checkpointPosition;

        public List<OperationAction> Actions => this.operationActions;

        public int CurrentPosition => this.operationActions.Count;

        public override EntityInstanceId Id { get; } = entityInstanceId;

        public void Commit()
        {
            this.checkpointPosition = this.CurrentPosition;
        }

        public void Rollback()
        {
            this.operationActions.RemoveRange(this.checkpointPosition, this.operationActions.Count - this.checkpointPosition);
        }

        public void Reset()
        {
            this.operationActions = new List<OperationAction>();
            this.checkpointPosition = 0;
        }

        public override void SignalEntity(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
        {
            Check.NotDefault(id);

            this.operationActions.Add(new SendSignalOperationAction()
            {
                InstanceId = id.ToString(),
                Name = operationName,
                Input = dataConverter.Serialize(input),
                ScheduledTime = options?.SignalTime?.UtcDateTime,
            });
        }

        public override string ScheduleNewOrchestration(TaskName name, object? input = null, StartOrchestrationOptions? options = null)
        {
            Check.NotEntity(true, options?.InstanceId);

            string instanceId = options?.InstanceId ?? Guid.NewGuid().ToString("N");
            this.operationActions.Add(new StartNewOrchestrationOperationAction()
            {
                Name = name.Name,
                Version = name.Version,
                InstanceId = instanceId,
                Input = dataConverter.Serialize(input),
                ScheduledStartTime = options?.StartAt?.UtcDateTime,
            });
            return instanceId;
        }
    }

    class OperationShim(TaskEntityShim taskEntityShim) : TaskEntityOperation
    {
        string? name;
        string? input;

        public override string Name => this.name!; // name is always set before user code can access this property

        public override TaskEntityContext Context => taskEntityShim.context;

        public override TaskEntityState State => taskEntityShim.state;

        public override bool HasInput => this.input != null;

        public override object? GetInput(Type inputType)
        {
            return taskEntityShim.dataConverter.Deserialize(this.input, inputType);
        }

        public void SetNameAndInput(string name, string? input)
        {
            this.name = name;
            this.input = input;
        }
    }
}
