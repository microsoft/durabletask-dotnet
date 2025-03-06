// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using CoreTaskActivity = DurableTask.Core.TaskActivity;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim.Core;

/// <summary>
/// A shim activity manager which allows for creating the actual activity in the middleware.
/// </summary>
sealed class ShimActivityManager : INameVersionObjectManager<CoreTaskActivity>
{
    /// <inheritdoc/>
    public void Add(ObjectCreator<CoreTaskActivity> creator) => throw new NotSupportedException();

    /// <inheritdoc/>
    public CoreTaskActivity? GetObject(string name, string? version) => new ShimTaskActivity();
}

/// <summary>
/// A shim task activity which allows for creating the actual activity in the middleware.
/// </summary>
sealed class ShimTaskActivity : CoreTaskActivity
{
    CoreTaskActivity? activity;

    /// <inheritdoc/>
    public override string Run(TaskContext context, string input) => throw new NotImplementedException();

    /// <inheritdoc/>
    public override Task<string> RunAsync(TaskContext context, string input)
    {
        Verify.NotNull(this.activity);
        return this.activity.RunAsync(context, input);
    }

    /// <summary>
    /// Sets the inner activity.
    /// </summary>
    /// <param name="activity">The activity to set.</param>
    internal void SetInnerActivity(CoreTaskActivity activity) => this.activity = Check.NotNull(activity);
}
