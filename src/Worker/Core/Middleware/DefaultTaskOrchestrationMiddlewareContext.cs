// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Microsoft.DurableTask.Worker.Middleware;

/// <summary>
/// Default implementation of <see cref="TaskOrchestrationMiddlewareContext"/>.
/// </summary>
internal sealed class DefaultTaskOrchestrationMiddlewareContext : TaskOrchestrationMiddlewareContext
{
    readonly Func<Task<object?>> body;
    int bodyInvoked;
    object? result;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTaskOrchestrationMiddlewareContext"/> class.
    /// </summary>
    /// <param name="name">The orchestration name.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    /// <param name="version">The orchestration version.</param>
    /// <param name="parent">The parent orchestration instance.</param>
    /// <param name="tags">The orchestration tags, when provided by a reliable source.</param>
    /// <param name="isReplaying">A value indicating whether the orchestration is replaying.</param>
    /// <param name="inputType">The declared orchestration input type.</param>
    /// <param name="input">The deserialized orchestration input.</param>
    /// <param name="rawInput">The raw orchestration input.</param>
    /// <param name="orchestrationContext">The orchestration context passed to the orchestrator.</param>
    /// <param name="features">The middleware feature collection.</param>
    /// <param name="services">The invocation service provider.</param>
    /// <param name="body">The orchestration body delegate.</param>
    public DefaultTaskOrchestrationMiddlewareContext(
        TaskName name,
        string instanceId,
        string version,
        ParentOrchestrationInstance? parent,
        IReadOnlyDictionary<string, string>? tags,
        bool isReplaying,
        Type inputType,
        object? input,
        string? rawInput,
        TaskOrchestrationContext orchestrationContext,
        IMiddlewareFeatures features,
        IServiceProvider services,
        Func<Task<object?>> body)
    {
        this.Name = Check.NotDefault(name);
        this.InstanceId = Check.NotNull(instanceId);
        this.Version = version;
        this.Parent = parent;
        this.Tags = CopyTags(tags);
        this.IsReplaying = isReplaying;
        this.InputType = Check.NotNull(inputType);
        this.Input = input;
        this.RawInput = rawInput;
        this.OrchestrationContext = Check.NotNull(orchestrationContext);
        this.Features = Check.NotNull(features);
        this.Services = Check.NotNull(services);
        this.body = Check.NotNull(body);
    }

    /// <inheritdoc/>
    public override TaskName Name { get; }

    /// <inheritdoc/>
    public override string InstanceId { get; }

    /// <inheritdoc/>
    public override string Version { get; }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent { get; }

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, string>? Tags { get; }

    /// <inheritdoc/>
    public override bool IsReplaying { get; }

    /// <inheritdoc/>
    public override Type InputType { get; }

    /// <inheritdoc/>
    public override object? Input { get; }

    /// <inheritdoc/>
    public override string? RawInput { get; }

    /// <inheritdoc/>
    public override TaskOrchestrationContext OrchestrationContext { get; }

    /// <inheritdoc/>
    public override IMiddlewareFeatures Features { get; }

    /// <inheritdoc/>
    public override CancellationToken CancellationToken => CancellationToken.None;

    /// <inheritdoc/>
    public override object? Result => this.result;

    /// <summary>
    /// Gets the invocation service provider used to resolve type-based middleware.
    /// </summary>
    internal IServiceProvider Services { get; }

    /// <summary>
    /// Gets a value indicating whether the orchestration body was invoked.
    /// </summary>
    internal bool BodyInvoked => Volatile.Read(ref this.bodyInvoked) != 0;

    /// <summary>
    /// Invokes the orchestration body.
    /// </summary>
    /// <returns>A task that completes when the body finishes.</returns>
    internal async Task InvokeBodyAsync()
    {
        if (Interlocked.Exchange(ref this.bodyInvoked, 1) != 0)
        {
            throw new InvalidOperationException(
                "Orchestration middleware must call the next delegate exactly once.");
        }

        this.result = await this.body();
    }

    static ReadOnlyDictionary<string, string>? CopyTags(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        Dictionary<string, string> copy = new();
        foreach (KeyValuePair<string, string> tag in tags)
        {
            copy.Add(tag.Key, tag.Value);
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
