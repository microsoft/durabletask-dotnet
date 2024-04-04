// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// Shims a <see cref="ITaskActivity" /> to a <see cref="TaskActivity" />.
/// </summary>
/// <param name="dataConverter">The data converter.</param>
/// <param name="name">The name of the activity.</param>
/// <param name="implementation">The activity implementation to wrap.</param>
class TaskActivityShim(DataConverter dataConverter, TaskName name, ITaskActivity implementation) : TaskActivity
{
    readonly ITaskActivity implementation = Check.NotNull(implementation);
    readonly DataConverter dataConverter = Check.NotNull(dataConverter);
    readonly TaskName name = Check.NotDefault(name);

    /// <inheritdoc/>
    public override async Task<string?> RunAsync(TaskContext coreContext, string? rawInput)
    {
        Check.NotNull(coreContext);
        string? strippedRawInput = StripArrayCharacters(rawInput);
        object? deserializedInput = this.dataConverter.Deserialize(strippedRawInput, this.implementation.InputType);
        TaskActivityContextWrapper contextWrapper = new(coreContext, this.name);
        object? output = await this.implementation.RunAsync(contextWrapper, deserializedInput);

        // Return the output (if any) as a serialized string.
        string? serializedOutput = this.dataConverter.Serialize(output);
        return serializedOutput;
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

    sealed class TaskActivityContextWrapper(TaskContext taskContext, TaskName name) : TaskActivityContext
    {
        public override TaskName Name => name;

        public override string InstanceId => taskContext.OrchestrationInstance.InstanceId;
    }
}
