// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Class-based versioned orchestrators used by integration tests.
/// </summary>
public static class VersionedClassSyntaxTestOrchestration
{
    /// <summary>
    /// Version 1 of the explicit version routing orchestration.
    /// </summary>
    [DurableTask("VersionedClassSyntax")]
    [DurableTaskVersion("v1")]
    public sealed class VersionedClassSyntaxV1 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => Task.FromResult($"v1:{input}");
    }

    /// <summary>
    /// Version 2 of the explicit version routing orchestration.
    /// </summary>
    [DurableTask("VersionedClassSyntax")]
    [DurableTaskVersion("v2")]
    public sealed class VersionedClassSyntaxV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => Task.FromResult($"v2:{input}");
    }

    /// <summary>
    /// Version 2 of the orchestration that explicitly targets an older activity version.
    /// </summary>
    [DurableTask("VersionedActivityOverrideOrchestration")]
    [DurableTaskVersion("v2")]
    public sealed class VersionedActivityOverrideOrchestrationV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => context.CallActivityAsync<string>(
                "VersionedActivityOverrideActivity",
                input,
                new ActivityOptions
                {
                    Version = "v1",
                });
    }

    /// <summary>
    /// Version 1 of the explicitly-versioned activity.
    /// </summary>
    [DurableTask("VersionedActivityOverrideActivity")]
    [DurableTaskVersion("v1")]
    public sealed class VersionedActivityOverrideActivityV1 : TaskActivity<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, int input)
            => Task.FromResult($"activity-v1:{input}");
    }

    /// <summary>
    /// Version 2 of the explicitly-versioned activity.
    /// </summary>
    [DurableTask("VersionedActivityOverrideActivity")]
    [DurableTaskVersion("v2")]
    public sealed class VersionedActivityOverrideActivityV2 : TaskActivity<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, int input)
            => Task.FromResult($"activity-v2:{input}");
    }

    /// <summary>
    /// Version 1 of the continue-as-new orchestration.
    /// </summary>
    [DurableTask("VersionedContinueAsNewClassSyntax")]
    [DurableTaskVersion("v1")]
    public sealed class VersionedContinueAsNewClassSyntaxV1 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
        {
            context.ContinueAsNew(new ContinueAsNewOptions
            {
                NewInput = input + 1,
                NewVersion = "v2",
            });

            return Task.FromResult(string.Empty);
        }
    }

    /// <summary>
    /// Version 2 of the continue-as-new orchestration.
    /// </summary>
    [DurableTask("VersionedContinueAsNewClassSyntax")]
    [DurableTaskVersion("v2")]
    public sealed class VersionedContinueAsNewClassSyntaxV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => Task.FromResult($"v2:{input}");
    }
}
