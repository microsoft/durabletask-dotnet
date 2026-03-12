// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// A plugin that validates input data before orchestrations and activities execute.
/// Users register validation rules via <see cref="IInputValidator"/> implementations.
/// If validation fails, an <see cref="ArgumentException"/> is thrown before the task runs.
/// </summary>
public sealed class ValidationPlugin : IDurableTaskPlugin
{
    /// <summary>
    /// The default plugin name.
    /// </summary>
    public const string DefaultName = "Microsoft.DurableTask.Validation";

    readonly IReadOnlyList<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly IReadOnlyList<IActivityInterceptor> activityInterceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationPlugin"/> class.
    /// </summary>
    /// <param name="validators">The input validators to use.</param>
    public ValidationPlugin(params IInputValidator[] validators)
    {
        Check.NotNull(validators);
        this.orchestrationInterceptors = new List<IOrchestrationInterceptor>
        {
            new ValidationOrchestrationInterceptor(validators),
        };
        this.activityInterceptors = new List<IActivityInterceptor>
        {
            new ValidationActivityInterceptor(validators),
        };
    }

    /// <inheritdoc />
    public string Name => DefaultName;

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    sealed class ValidationOrchestrationInterceptor : IOrchestrationInterceptor
    {
        readonly IInputValidator[] validators;

        public ValidationOrchestrationInterceptor(IInputValidator[] validators) => this.validators = validators;

        public async Task OnOrchestrationStartingAsync(OrchestrationInterceptorContext context)
        {
            foreach (IInputValidator validator in this.validators)
            {
                ValidationResult result = await validator.ValidateAsync(context.Name, context.Input);
                if (!result.IsValid)
                {
                    throw new ArgumentException(
                        $"Input validation failed for orchestration '{context.Name}': {result.ErrorMessage}");
                }
            }
        }

        public Task OnOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result) =>
            Task.CompletedTask;

        public Task OnOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception) =>
            Task.CompletedTask;
    }

    sealed class ValidationActivityInterceptor : IActivityInterceptor
    {
        readonly IInputValidator[] validators;

        public ValidationActivityInterceptor(IInputValidator[] validators) => this.validators = validators;

        public async Task OnActivityStartingAsync(ActivityInterceptorContext context)
        {
            foreach (IInputValidator validator in this.validators)
            {
                ValidationResult result = await validator.ValidateAsync(context.Name, context.Input);
                if (!result.IsValid)
                {
                    throw new ArgumentException(
                        $"Input validation failed for activity '{context.Name}': {result.ErrorMessage}");
                }
            }
        }

        public Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result) =>
            Task.CompletedTask;

        public Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception) =>
            Task.CompletedTask;
    }
}
