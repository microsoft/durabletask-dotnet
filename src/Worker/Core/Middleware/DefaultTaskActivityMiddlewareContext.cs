// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// Default implementation of <see cref="TaskActivityMiddlewareContext"/>.
/// </summary>
internal sealed class DefaultTaskActivityMiddlewareContext : TaskActivityMiddlewareContext
{
    readonly Func<Task<object?>> body;
    object? result;
    bool resultSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTaskActivityMiddlewareContext"/> class.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    /// <param name="inputType">The declared activity input type.</param>
    /// <param name="input">The deserialized activity input.</param>
    /// <param name="rawInput">The raw activity input.</param>
    /// <param name="activityContext">The activity context passed to the activity.</param>
    /// <param name="features">The middleware feature collection.</param>
    /// <param name="services">The invocation service provider.</param>
    /// <param name="body">The activity body delegate.</param>
    public DefaultTaskActivityMiddlewareContext(
        TaskName name,
        string instanceId,
        Type inputType,
        object? input,
        string? rawInput,
        TaskActivityContext activityContext,
        IMiddlewareFeatures features,
        IServiceProvider services,
        Func<Task<object?>> body)
    {
        this.Name = Check.NotDefault(name);
        this.InstanceId = Check.NotNull(instanceId);
        this.InputType = Check.NotNull(inputType);
        this.Input = input;
        this.RawInput = rawInput;
        this.ActivityContext = Check.NotNull(activityContext);
        this.Features = Check.NotNull(features);
        this.Services = Check.NotNull(services);
        this.body = Check.NotNull(body);
    }

    /// <inheritdoc/>
    public override TaskName Name { get; }

    /// <inheritdoc/>
    public override string InstanceId { get; }

    /// <inheritdoc/>
    public override Type InputType { get; }

    /// <inheritdoc/>
    public override object? Input { get; }

    /// <inheritdoc/>
    public override string? RawInput { get; }

    /// <inheritdoc/>
    public override TaskActivityContext ActivityContext { get; }

    /// <inheritdoc/>
    public override IMiddlewareFeatures Features { get; }

    /// <inheritdoc/>
    public override IServiceProvider Services { get; }

    /// <inheritdoc/>
    public override CancellationToken CancellationToken => CancellationToken.None;

    /// <inheritdoc/>
    public override object? Result => this.result;

    /// <inheritdoc/>
    public override void SetResult(object? result)
    {
        this.result = result;
        this.resultSet = true;
    }

    /// <summary>
    /// Invokes the activity body when no middleware already set a result.
    /// </summary>
    /// <returns>A task that completes when the body finishes.</returns>
    internal async Task InvokeBodyAsync()
    {
        if (!this.resultSet)
        {
            this.SetResult(await this.body());
        }
    }
}
