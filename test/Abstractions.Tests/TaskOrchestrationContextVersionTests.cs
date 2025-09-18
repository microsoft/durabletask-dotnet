// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Abstractions.Tests;
public class TaskOrchestrationContextVersionTests
{
    [Theory]
    [InlineData("1.1", "1.0", 1)]
    [InlineData("1.0", "1.1", -1)]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("", "1.0", -1)]
    [InlineData("1.0", "", 1)]
    [InlineData("", "", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("alpha", "beta", -1)]
    [InlineData("beta", "alpha", 1)]
    [InlineData("alpha", "alpha", 0)]
    public void OrchestrationContext_Version_ComparisonTests(string orchestrationVersion, string otherVersion, int comparisonResult)
    {
        TaskOrchestrationContext orchestrationContext = new TestTaskOrchestrationContext(orchestrationVersion);

        if (comparisonResult > 0)
        {
            orchestrationContext.CompareVersionTo(otherVersion).Should().BeGreaterThan(0);
        }
        else if (comparisonResult < 0)
        {
            orchestrationContext.CompareVersionTo(otherVersion).Should().BeLessThan(0);
        }
        else
        {
            orchestrationContext.CompareVersionTo(otherVersion).Should().Be(0);
        }
    }

    class TestTaskOrchestrationContext : TaskOrchestrationContext
    {
        internal readonly string version = string.Empty;

        public TestTaskOrchestrationContext(string version)
        {
            this.version = version;
        }
        public override TaskName Name => throw new NotImplementedException();

        public override string InstanceId => throw new NotImplementedException();

        public override ParentOrchestrationInstance? Parent => throw new NotImplementedException();

        public override DateTime CurrentUtcDateTime => throw new NotImplementedException();

        public override bool IsReplaying => throw new NotImplementedException();

        public override string Version => this.version;

        protected override ILoggerFactory LoggerFactory => throw new NotImplementedException();

        public override Dictionary<string, object?> Properties => throw new NotImplementedException();

        public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, object? input = null, TaskOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
        {
            throw new NotImplementedException();
        }

        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public override T? GetInput<T>() where T : default
        {
            throw new NotImplementedException();
        }

        public override Guid NewGuid()
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(string instanceId, string eventName, object payload)
        {
            throw new NotImplementedException();
        }

        public override void SetCustomStatus(object? customStatus)
        {
            throw new NotImplementedException();
        }

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
