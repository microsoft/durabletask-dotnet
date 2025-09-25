// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.InProcessTestFramework;

/// <summary>
/// A simple mock implementation of TaskActivityContext for activity execution.
/// Provides the activity name and a generic instance ID for in-process testing.
/// </summary>
internal class MockActivityContext : TaskActivityContext
{
    readonly TaskName name;

    public MockActivityContext(string activityName)
    {
        this.name = new TaskName(activityName);
    }

    public override TaskName Name => this.name;
    
    // Instance id is not used in the in-process test framework, so we use a fixed value.
    public override string InstanceId => "in-process-test";
}
