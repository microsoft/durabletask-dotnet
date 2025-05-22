// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dapr.DurableTask.Worker.Shims;

public class TaskOrchestrationContextWrapperTests
{
    [Fact]
    public void Ctor_NullParent_Populates()
    {
        TestOrchestrationContext innerContext = new();
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, null);
        string input = "test-input";

        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, input);

        VerifyWrapper(wrapper, innerContext, invocationContext, input);
    }

    [Fact]
    public void Ctor_NonNullParent_Populates()
    {
        TestOrchestrationContext innerContext = new();
        ParentOrchestrationInstance parent = new("Parent", Guid.NewGuid().ToString());
        OrchestrationInvocationContext invocationContext = new("Test", new(), NullLoggerFactory.Instance, parent);
        string input = "test-input";

        TaskOrchestrationContextWrapper wrapper = new(innerContext, invocationContext, input);

        VerifyWrapper(wrapper, innerContext, invocationContext, input);
    }

    static void VerifyWrapper<T>(
        TaskOrchestrationContextWrapper wrapper,
        OrchestrationContext innerContext,
        OrchestrationInvocationContext invocationContext,
        T input)
    {
        wrapper.Name.Should().Be(invocationContext.Name);
        wrapper.InstanceId.Should().Be(innerContext.OrchestrationInstance.InstanceId);
        wrapper.Parent.Should().Be(invocationContext.Parent);
        wrapper.IsReplaying.Should().Be(false);
        wrapper.GetInput<T>().Should().Be(input);
    }

    class TestOrchestrationContext : OrchestrationContext
    {
        public TestOrchestrationContext()
        {
            this.OrchestrationInstance = new()
            {
                InstanceId = Guid.NewGuid().ToString(),
                ExecutionId = Guid.NewGuid().ToString(),
            };
        }

        public override void ContinueAsNew(object input)
        {
            throw new NotImplementedException();
        }

        public override void ContinueAsNew(string newVersion, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateSubOrchestrationInstance<T>(
            string name, string version, string instanceId, object input, IDictionary<string, string> tags)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state)
        {
            throw new NotImplementedException();
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        public override Task<TResult> ScheduleTask<TResult>(string name, string version, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData)
        {
            throw new NotImplementedException();
        }
    }
}