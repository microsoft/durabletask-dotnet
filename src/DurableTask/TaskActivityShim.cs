// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;

namespace Microsoft.DurableTask;

class TaskActivityShim<TInput, TOutput> : TaskActivityShim
{
    public TaskActivityShim(
        DataConverter dataConverter,
        TaskName name,
        Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
        : base(dataConverter, name, new LambdaActivity(implementation))
    {
    }

    sealed class LambdaActivity : TaskActivityBase<TInput, TOutput>
    {
        readonly Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation;

        public LambdaActivity(Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
        {
            this.implementation = implementation;
        }

        protected override Task<TOutput?> OnRunAsync(TaskActivityContext context, TInput? input)
        {
            return this.implementation(context, input);
        }
    }
}

class TaskActivityShim : TaskActivity
{
    readonly ITaskActivity implementation;
    readonly DataConverter dataConverter;
    readonly TaskName name;

    public TaskActivityShim(
        DataConverter dataConverter,
        TaskName name,
        ITaskActivity implementation)
    {
        this.dataConverter = dataConverter;
        this.name = name;
        this.implementation = implementation;
    }

    public override async Task<string?> RunAsync(TaskContext coreContext, string? rawInput)
    {
        string? strippedRawInput = StripArrayCharacters(rawInput);
        object? deserializedInput = this.dataConverter.Deserialize(strippedRawInput, this.implementation.InputType);
        TaskActivityContextWrapper contextWrapper = new(coreContext, this.name);
        object? output = await this.implementation.RunAsync(contextWrapper, deserializedInput);

        // Return the output (if any) as a serialized string.
        string? serializedOutput = this.dataConverter.Serialize(output);
        return serializedOutput;
    }

    // Not used/called
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

        public TaskActivityContextWrapper(
            TaskContext taskContext,
            TaskName name)
        {
            this.innerContext = taskContext;
            this.name = name;
        }

        public override TaskName Name => this.name;

        public override string InstanceId => this.innerContext.OrchestrationInstance.InstanceId;
    }
}
