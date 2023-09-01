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
/// A wrapper to go from <see cref="OrchestrationEntityContext" /> to <see cref="TaskOrchestrationEntityFeature "/>.
/// </summary>
sealed class TaskOrchestrationEntityContext : TaskOrchestrationEntityFeature
{
    static readonly List<EntityInstanceId> EmptyEntityList = new List<EntityInstanceId>();

    readonly TaskOrchestrationContextWrapper taskOrchestrationContext;
    readonly OrchestrationContext innerContext;
    readonly string instanceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrationEntityContext"/> class.
    /// </summary>
    /// <param name="taskOrchestrationContext">The wrapper for the orchestration context.</param>
    /// <param name="innerContext">The inner orchestration entity context.</param>
    public TaskOrchestrationEntityContext(TaskOrchestrationContextWrapper taskOrchestrationContext, OrchestrationContext innerContext)
    {
        this.taskOrchestrationContext = taskOrchestrationContext;
        this.innerContext = innerContext;
        this.instanceId = taskOrchestrationContext.InstanceId;
        this.EntityContext = new OrchestrationEntityContext(taskOrchestrationContext.InstanceId, innerContext.OrchestrationInstance.ExecutionId, innerContext);
    }

    /// <summary>
    /// Gets the entity context for the orchestration, which stores all the entity-related orchestration state.
    /// </summary>
    internal OrchestrationEntityContext EntityContext { get; }

    /// <inheritdoc/>
    public override async Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds)
    {
        if (!this.EntityContext.ValidateAcquireTransition(out string? errormsg))
        {
            throw new InvalidOperationException(errormsg);
        }

        var dtEntities = entityIds.Select(x => new DurableTaskCore.Entities.EntityId(x.Name, x.Key)).ToArray();

        if (dtEntities.Length == 0)
        {
            throw new ArgumentException("The list of entities to lock must not be empty.", nameof(entityIds));
        }

        // use a deterministically replayable unique ID for this lock request, and to receive the response
        var criticalSectionId = this.taskOrchestrationContext.NewGuid();

        // send a message to the first entity to be acquired
        var eventToSend = this.EntityContext.EmitAcquireMessage(criticalSectionId, dtEntities);

        if (!this.taskOrchestrationContext.IsReplaying)
        {
            // this.Config.TraceHelper.SendingEntityMessage(
            //    this.InstanceId,
            //    this.ExecutionId,
            //    eventToSend.TargetInstance.InstanceId,
            //    eventToSend.EventName,
            //    eventToSend.EventContent);
        }

        this.innerContext.SendEvent(eventToSend.TargetInstance, eventToSend.EventName, eventToSend.EventContent);

        OperationResult result = await this.taskOrchestrationContext.WaitForExternalEvent<OperationResult>(criticalSectionId.ToString());

        this.EntityContext.CompleteAcquire(result, criticalSectionId);

        // return an IDisposable that releases the lock
        return new LockReleaser(this, criticalSectionId);
    }

    /// <inheritdoc/>
    public override async Task<TResult> CallEntityAsync<TResult>(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null)
    {
        var operationResult = await this.SignalOrCallEntityInternalAsync(id, operationName, input, oneWay: false, scheduledTime: null);

        if (operationResult.ErrorMessage != null)
        {
            throw new OperationFailedException(id, operationName, operationResult.ErrorMessage, ConvertFailureDetails(operationResult.FailureDetails!));
        }
        else
        {
            return this.taskOrchestrationContext.DataConverter.Deserialize<TResult>(operationResult.Result!);
        }
    }

    /// <inheritdoc/>
    public override async Task CallEntityAsync(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null)
    {
        var operationResult = await this.SignalOrCallEntityInternalAsync(id, operationName, input, oneWay: false, scheduledTime: null);

        if (operationResult.ErrorMessage != null)
        {
            throw new OperationFailedException(id, operationName, operationResult.ErrorMessage, ConvertFailureDetails(operationResult.FailureDetails!));
        }
    }

    /// <inheritdoc/>
    public override async Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null)
    {
        await this.SignalOrCallEntityInternalAsync(id, operationName, input, oneWay: true, scheduledTime: options?.SignalTime);
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
            foreach (var releaseMessage in this.EntityContext.EmitLockReleaseMessages())
            {
                if (!this.taskOrchestrationContext.IsReplaying)
                {
                    // this.Config.TraceHelper.SendingEntityMessage(
                    //    this.InstanceId,
                    //    this.ExecutionId,
                    //    releaseMessage.TargetInstance.InstanceId,
                    //    releaseMessage.EventName,
                    //    releaseMessage.EventContent);
                }

                this.innerContext.SendEvent(releaseMessage.TargetInstance, releaseMessage.EventName, releaseMessage.EventContent);
            }
        }
    }

    static (DateTime Original, DateTime Capped) ConvertDateTime(DateTimeOffset original)
    {
        // we don't know the actual max delay, because we don't have backend information, so we choose
        // a value conservatively that should work for all backends
        TimeSpan maxDelay = TimeSpan.FromDays(6);

        DateTime utcNow = DateTime.UtcNow;
        DateTime originalUtc = original.UtcDateTime;
        DateTime cappedUtc = (originalUtc - utcNow <= maxDelay) ? originalUtc : utcNow + maxDelay;
        return (originalUtc, cappedUtc);
    }

    static TaskFailureDetails ConvertFailureDetails(FailureDetails failureDetails)
     => new TaskFailureDetails(
         failureDetails.ErrorType,
         failureDetails.ErrorMessage,
         failureDetails.StackTrace,
         failureDetails.InnerFailure != null ? ConvertFailureDetails(failureDetails.InnerFailure) : null);

    async ValueTask<OperationResult> SignalOrCallEntityInternalAsync(EntityInstanceId id, string operationName, object? input, bool oneWay = false, DateTimeOffset? scheduledTime = null)
    {
        if (!this.EntityContext.ValidateOperationTransition(id.ToString(), oneWay, out string? errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        var guid = this.taskOrchestrationContext.NewGuid(); // deterministically replayable unique id for this request
        string operationId = guid.ToString();
        string? serializedInput = this.taskOrchestrationContext.DataConverter.Serialize(input);
        var target = new OrchestrationInstance() { InstanceId = id.ToString() };

        var eventToSend = this.EntityContext.EmitRequestMessage(
                target,
                operationName,
                oneWay,
                guid,
                scheduledTime.HasValue ? ConvertDateTime(scheduledTime.Value) : null,
                serializedInput);

        if (!this.taskOrchestrationContext.IsReplaying)
        {
            // this.Config.TraceHelper.SendingEntityMessage(
            //    this.InstanceId,
            //    this.ExecutionId,
            //    target.InstanceId,
            //    eventToSend.EventName,
            //    eventToSend.EventContent);
        }

        this.innerContext.SendEvent(eventToSend.TargetInstance, eventToSend.EventName, eventToSend.EventContent);

        if (!oneWay)
        {
            OperationResult response = await this.taskOrchestrationContext.WaitForExternalEvent<OperationResult>(guid.ToString());

            if (this.EntityContext.IsInsideCriticalSection)
            {
                // the lock is available again now that the entity call returned
                this.EntityContext.RecoverLockAfterCall(target.InstanceId);
            }

            return response;
        }
        else
        {
            return null!; // ignored by caller
        }
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
