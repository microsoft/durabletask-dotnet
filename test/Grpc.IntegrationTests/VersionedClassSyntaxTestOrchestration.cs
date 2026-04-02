// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Class-based versioned orchestrators used by integration tests.
/// </summary>
public static class VersionedClassSyntaxTestOrchestration
{
    public const string ExplicitVersionTagName = "microsoft.durabletask.activity.explicit-version";

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
    /// Version 2 of the orchestration that explicitly requests a missing activity version.
    /// </summary>
    [DurableTask("ExplicitActivityVersionNoFallbackOrchestration")]
    [DurableTaskVersion("v2")]
    public sealed class ExplicitActivityVersionNoFallbackOrchestrationV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => context.CallActivityAsync<string>(
                "ExplicitActivityVersionNoFallbackActivity",
                input,
                new ActivityOptions
                {
                    Version = "v1",
                });
    }

    /// <summary>
    /// Unversioned activity used to verify explicit version requests do not silently fall back.
    /// </summary>
    [DurableTask("ExplicitActivityVersionNoFallbackActivity")]
    public sealed class UnversionedActivityVersionNoFallbackActivity : TaskActivity<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, int input)
            => Task.FromResult($"activity-unversioned:{input}");
    }

    /// <summary>
    /// Version 2 of the orchestration that inherits its version when calling an unversioned activity.
    /// </summary>
    [DurableTask("InheritedActivityVersionFallbackOrchestration")]
    [DurableTaskVersion("v2")]
    public sealed class InheritedActivityVersionFallbackOrchestrationV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => context.CallActivityAsync<string>("InheritedActivityVersionFallbackActivity", input);
    }

    /// <summary>
    /// Unversioned activity used to verify inherited activity routing retains compatibility fallback behavior.
    /// </summary>
    [DurableTask("InheritedActivityVersionFallbackActivity")]
    public sealed class UnversionedInheritedActivityVersionFallbackActivity : TaskActivity<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, int input)
            => Task.FromResult($"activity-unversioned:{input}");
    }

    /// <summary>
    /// Version 2 of the orchestration that passes the reserved explicit-version tag in user-supplied task options.
    /// </summary>
    [DurableTask("SpoofedActivityVersionTagFallbackOrchestration")]
    [DurableTaskVersion("v2")]
    public sealed class SpoofedActivityVersionTagFallbackOrchestrationV2 : TaskOrchestrator<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskOrchestrationContext context, int input)
            => context.CallActivityAsync<string>(
                "SpoofedActivityVersionTagFallbackActivity",
                input,
                new TaskOptions(tags: new Dictionary<string, string>
                {
                    [ExplicitVersionTagName] = bool.FalseString,
                }));
    }

    /// <summary>
    /// Unversioned activity used to verify user-supplied reserved tags do not disable compatibility fallback behavior.
    /// </summary>
    [DurableTask("SpoofedActivityVersionTagFallbackActivity")]
    public sealed class UnversionedSpoofedActivityVersionTagFallbackActivity : TaskActivity<int, string>
    {
        /// <inheritdoc />
        public override Task<string> RunAsync(TaskActivityContext context, int input)
            => Task.FromResult($"activity-unversioned:{input}");
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
