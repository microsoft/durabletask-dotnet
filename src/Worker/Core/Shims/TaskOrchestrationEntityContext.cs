// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DurableTask.Core;
using DurableTask.Core.Entities;
using DurableTask.Core.Entities.OperationFormat;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using DurableTaskCore = DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A wrapper to go from <see cref="OrchestrationContext" /> to <see cref="TaskOrchestrationContext "/>.
/// </summary>
sealed partial class TaskOrchestrationContextWrapper
{
    /// <summary>
    /// A wrapper to go from <see cref="OrchestrationEntityContext" /> to <see cref="TaskOrchestrationEntityFeature "/>.
    /// </summary>
    sealed class TaskOrchestrationEntityContext : TaskOrchestrationEntityFeature
    {
        static readonly List<EntityInstanceId> EmptyEntityList = new();

        readonly TaskOrchestrationContextWrapper wrapper;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskOrchestrationEntityContext"/> class.
        /// </summary>
        /// <param name="taskOrchestrationContextWrapper">The wrapper for the orchestration context.</param>
        public TaskOrchestrationEntityContext(TaskOrchestrationContextWrapper taskOrchestrationContextWrapper)
        {
            this.wrapper = taskOrchestrationContextWrapper;
            this.EntityContext = new OrchestrationEntityContext(this.wrapper.InstanceId, this.wrapper.innerContext.OrchestrationInstance.ExecutionId, this.wrapper.innerContext);
        }

        /// <summary>
        /// Gets the entity context for the orchestration, which stores all the entity-related orchestration state.
        /// </summary>
        internal OrchestrationEntityContext EntityContext { get; }

        /// <inheritdoc/>
        public override async Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds)
        {
            Check.NotNull(entityIds);

            EntityId[] dtEntities = entityIds.Select(x => new DurableTaskCore.Entities.EntityId(x.Name, x.Key)).ToArray();

            if (dtEntities.Length == 0)
            {
                throw new ArgumentException("The list of entities to lock must not be empty.", nameof(entityIds));
            }

            if (!this.EntityContext.ValidateAcquireTransition(out string? errormsg))
            {
                throw new InvalidOperationException(errormsg);
            }

            // use a deterministically replayable unique ID for this lock request, and to receive the response
            Guid criticalSectionId = this.wrapper.NewGuid();

            // send a message to the first entity to be acquired
            EntityMessageEvent entityMessageEvent = this.EntityContext.EmitAcquireMessage(criticalSectionId, dtEntities);

            if (!this.wrapper.IsReplaying)
            {
                // this.Config.TraceHelper.SendingEntityMessage(
                //    this.InstanceId,
                //    this.ExecutionId,
                //    entityMessageEvent.TargetInstance.InstanceId,
                //    entityMessageEvent.EventName,
                //    entityMessageEvent.ToString());
            }

            this.wrapper.innerContext.SendEvent(entityMessageEvent.TargetInstance, entityMessageEvent.EventName, entityMessageEvent.ContentAsObject());

            OperationResult result = await this.wrapper.WaitForExternalEvent<OperationResult>(criticalSectionId.ToString());

            this.EntityContext.CompleteAcquire(result, criticalSectionId);

            // return an IDisposable that releases the lock
            return new LockReleaser(this, criticalSectionId);
        }

        /// <inheritdoc/>
        public override async Task<TResult> CallEntityAsync<TResult>(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null)
        {
            OperationResult operationResult = await this.CallEntityInternalAsync(id, operationName, input);

            if (operationResult.ErrorMessage != null)
            {
                throw new EntityOperationFailedException(id, operationName, operationResult.ErrorMessage, ConvertFailureDetails(operationResult.FailureDetails!));
            }
            else
            {
                return this.wrapper.DataConverter.Deserialize<TResult>(operationResult.Result!);
            }
        }

        /// <inheritdoc/>
        public override async Task CallEntityAsync(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null)
        {
            OperationResult operationResult = await this.CallEntityInternalAsync(id, operationName, input);

            if (operationResult.ErrorMessage != null)
            {
                throw new EntityOperationFailedException(id, operationName, operationResult.ErrorMessage, ConvertFailureDetails(operationResult.FailureDetails!));
            }
        }

        /// <inheritdoc/>
        public override Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
        {
            this.SendOperationMessage(id.ToString(), operationName, input, oneWay: true, scheduledTime: options?.SignalTime);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override bool InCriticalSection([NotNullWhen(true)] out IReadOnlyList<EntityInstanceId>? entityIds)
        {
            if (this.EntityContext.IsInsideCriticalSection)
            {
                entityIds = this.EntityContext.GetAvailableEntities().Select(x => new EntityInstanceId(x.Name, x.Key)).ToList();
                return true;
            }
            else
            {
                entityIds = EmptyEntityList;
                return false;
            }
        }

        /// <summary>
        /// exits the critical section, if currently within a critical section. Otherwise, this has no effect.
        /// </summary>
        /// <param name="matchCriticalSectionId">exit the critical section only if the critical section ID matches.</param>
        public void ExitCriticalSection(Guid? matchCriticalSectionId = null)
        {
            if (this.EntityContext.IsInsideCriticalSection
                && (matchCriticalSectionId == null || matchCriticalSectionId == this.EntityContext.CurrentCriticalSectionId))
            {
                foreach (EntityMessageEvent releaseMessage in this.EntityContext.EmitLockReleaseMessages())
                {
                    if (!this.wrapper.IsReplaying)
                    {
                        // this.Config.TraceHelper.SendingEntityMessage(
                        //    this.InstanceId,
                        //    this.ExecutionId,
                        //    releaseMessage.TargetInstance.InstanceId,
                        //    releaseMessage.EventName,
                        //    releaseMessage.EventContent);
                    }

                    this.wrapper.innerContext.SendEvent(releaseMessage.TargetInstance, releaseMessage.EventName, releaseMessage.ContentAsObject());
                }
            }
        }

        static TaskFailureDetails ConvertFailureDetails(FailureDetails failureDetails)
         => new(
             failureDetails.ErrorType,
             failureDetails.ErrorMessage,
             failureDetails.StackTrace,
             failureDetails.InnerFailure != null ? ConvertFailureDetails(failureDetails.InnerFailure) : null);

        async Task<OperationResult> CallEntityInternalAsync(EntityInstanceId id, string operationName, object? input)
        {
            string instanceId = id.ToString();
            Guid requestId = this.SendOperationMessage(instanceId, operationName, input, oneWay: false, scheduledTime: null);

            OperationResult response = await this.wrapper.WaitForExternalEvent<OperationResult>(requestId.ToString());

            if (this.EntityContext.IsInsideCriticalSection)
            {
                // the lock is available again now that the entity call returned
                this.EntityContext.RecoverLockAfterCall(id.ToString());
            }

            return response;
        }

        Guid SendOperationMessage(string instanceId, string operationName, object? input, bool oneWay, DateTimeOffset? scheduledTime)
        {
            if (!this.EntityContext.ValidateOperationTransition(instanceId, oneWay, out string? errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            Guid guid = this.wrapper.NewGuid(); // deterministically replayable unique id for this request
            string? serializedInput = this.wrapper.DataConverter.Serialize(input);
            var target = new OrchestrationInstance() { InstanceId = instanceId };

            EntityMessageEvent entityMessageEvent = this.EntityContext.EmitRequestMessage(
                    target,
                    operationName,
                    oneWay,
                    guid,
                    EntityMessageEvent.GetCappedScheduledTime(this.wrapper.innerContext.CurrentUtcDateTime, this.wrapper.invocationContext.Options.MaximumTimerInterval, scheduledTime?.UtcDateTime),
                    serializedInput);

            if (!this.wrapper.IsReplaying)
            {
                // this.Config.TraceHelper.SendingEntityMessage(
                //    this.InstanceId,
                //    this.ExecutionId,
                //    target.InstanceId,
                //    entityMessageEvent.EventName,
                //    entityMessageEvent.ToString());
            }

            this.wrapper.innerContext.SendEvent(entityMessageEvent.TargetInstance, entityMessageEvent.EventName, entityMessageEvent.ContentAsObject());

            return guid;
        }

        class LockReleaser : IAsyncDisposable
        {
            readonly TaskOrchestrationEntityContext context;
            readonly Guid criticalSectionId;

            public LockReleaser(TaskOrchestrationEntityContext context, Guid criticalSectionId)
            {
                this.context = context;
                this.criticalSectionId = criticalSectionId;
            }

            public ValueTask DisposeAsync()
            {
                this.context.ExitCriticalSection(this.criticalSectionId);
                return default;
            }
        }
    }
}
