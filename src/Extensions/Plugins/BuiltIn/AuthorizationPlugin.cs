// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins.BuiltIn;

/// <summary>
/// A plugin that performs authorization checks before orchestrations and activities
/// are executed. Users define authorization rules via <see cref="IAuthorizationHandler"/>.
/// If a rule denies access, a <see cref="UnauthorizedAccessException"/> is thrown.
/// </summary>
public sealed class AuthorizationPlugin : IDurableTaskPlugin
{
    /// <summary>
    /// The default plugin name.
    /// </summary>
    public const string DefaultName = "Microsoft.DurableTask.Authorization";

    readonly IReadOnlyList<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly IReadOnlyList<IActivityInterceptor> activityInterceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationPlugin"/> class.
    /// </summary>
    /// <param name="handler">The authorization handler to evaluate.</param>
    public AuthorizationPlugin(IAuthorizationHandler handler)
    {
        Check.NotNull(handler);
        this.orchestrationInterceptors = new List<IOrchestrationInterceptor>
        {
            new AuthorizationOrchestrationInterceptor(handler),
        };
        this.activityInterceptors = new List<IActivityInterceptor>
        {
            new AuthorizationActivityInterceptor(handler),
        };
    }

    /// <inheritdoc />
    public string Name => DefaultName;

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    /// <inheritdoc />
    public void RegisterTasks(DurableTaskRegistry registry)
    {
        // Authorization plugin is cross-cutting only; it does not register any tasks.
    }

    sealed class AuthorizationOrchestrationInterceptor : IOrchestrationInterceptor
    {
        readonly IAuthorizationHandler handler;

        public AuthorizationOrchestrationInterceptor(IAuthorizationHandler handler) => this.handler = handler;

        public async Task OnOrchestrationStartingAsync(OrchestrationInterceptorContext context)
        {
            AuthorizationContext authContext = new(
                context.Name,
                context.InstanceId,
                AuthorizationTargetType.Orchestration,
                context.Input);

            if (!await this.handler.AuthorizeAsync(authContext))
            {
                throw new UnauthorizedAccessException(
                    $"Authorization denied for orchestration '{context.Name}' (instance '{context.InstanceId}').");
            }
        }

        public Task OnOrchestrationCompletedAsync(OrchestrationInterceptorContext context, object? result) =>
            Task.CompletedTask;

        public Task OnOrchestrationFailedAsync(OrchestrationInterceptorContext context, Exception exception) =>
            Task.CompletedTask;
    }

    sealed class AuthorizationActivityInterceptor : IActivityInterceptor
    {
        readonly IAuthorizationHandler handler;

        public AuthorizationActivityInterceptor(IAuthorizationHandler handler) => this.handler = handler;

        public async Task OnActivityStartingAsync(ActivityInterceptorContext context)
        {
            AuthorizationContext authContext = new(
                context.Name,
                context.InstanceId,
                AuthorizationTargetType.Activity,
                context.Input);

            if (!await this.handler.AuthorizeAsync(authContext))
            {
                throw new UnauthorizedAccessException(
                    $"Authorization denied for activity '{context.Name}' (instance '{context.InstanceId}').");
            }
        }

        public Task OnActivityCompletedAsync(ActivityInterceptorContext context, object? result) =>
            Task.CompletedTask;

        public Task OnActivityFailedAsync(ActivityInterceptorContext context, Exception exception) =>
            Task.CompletedTask;
    }
}
