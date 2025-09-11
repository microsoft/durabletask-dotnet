// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shims a <see cref="ITaskActivity" /> to a <see cref="TaskActivity" />.
/// </summary>
class TaskActivityShim : TaskActivity
{
    readonly ITaskActivity implementation;
    readonly ILogger logger;
    readonly DataConverter dataConverter;
    readonly TaskName name;
    readonly bool enableLargePayloadSupport;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskActivityShim"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="dataConverter">The data converter.</param>
    /// <param name="name">The name of the activity.</param>
    /// <param name="implementation">The activity implementation to wrap.</param>
    /// <param name="enableLargePayloadSupport">Whether to use async serialization for large payloads.</param>
    public TaskActivityShim(
        ILoggerFactory loggerFactory,
        DataConverter dataConverter,
        TaskName name,
        ITaskActivity implementation,
        bool enableLargePayloadSupport = false)
    {
        this.logger = Logs.CreateWorkerLogger(Check.NotNull(loggerFactory), "Activities");
        this.dataConverter = Check.NotNull(dataConverter);
        this.name = Check.NotDefault(name);
        this.implementation = Check.NotNull(implementation);
        this.enableLargePayloadSupport = enableLargePayloadSupport;
    }

    /// <inheritdoc/>
    public override async Task<string?> RunAsync(TaskContext coreContext, string? rawInput)
    {
        Check.NotNull(coreContext);
        string? strippedRawInput = StripArrayCharacters(rawInput);
        object? deserializedInput = this.enableLargePayloadSupport
            ? await this.dataConverter.DeserializeAsync(strippedRawInput, this.implementation.InputType)
            : this.dataConverter.Deserialize(strippedRawInput, this.implementation.InputType);
        TaskActivityContextWrapper contextWrapper = new(coreContext, this.name);

        string instanceId = coreContext.OrchestrationInstance.InstanceId;
        this.logger.ActivityStarted(instanceId, this.name);

        try
        {
            object? output = await this.implementation.RunAsync(contextWrapper, deserializedInput);

            // Return the output (if any) as a serialized string.
            string? serializedOutput = this.enableLargePayloadSupport
                ? await this.dataConverter.SerializeAsync(output)
                : this.dataConverter.Serialize(output);
            this.logger.ActivityCompleted(instanceId, this.name);

            return serializedOutput;
        }
        catch (Exception e)
        {
            this.logger.ActivityFailed(e, instanceId, this.name);
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Not used/called.</remarks>
    public override string Run(TaskContext context, string input) => throw new NotImplementedException();

    static string? StripArrayCharacters(string? input)
    {
        if (input != null && input.StartsWith('[') && input.EndsWith(']'))
        {
            // Strip the outer bracket characters
            return input[1..^1];
        }

        return input;
    }

    sealed class TaskActivityContextWrapper : TaskActivityContext
    {
        readonly TaskContext innerContext;
        readonly TaskName name;

        public TaskActivityContextWrapper(TaskContext taskContext, TaskName name)
        {
            this.innerContext = taskContext;
            this.name = name;
        }

        public override TaskName Name => this.name;

        public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;
    }
}
