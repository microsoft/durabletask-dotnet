// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Tests;

public partial class DurableTaskRegistryTests
{
    [Fact]
    public void AddOrchestrator_DefaultName_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddOrchestrator(default, () => new TestOrchestrator());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");

        act = () => registry.AddOrchestrator(string.Empty, () => new TestOrchestrator());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");

        act = () => registry.AddOrchestrator(null!, () => new TestOrchestrator());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddOrchestrator_Factory_Success()
    {
        TestOrchestrator orchestrator = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(
            r => r.AddOrchestrator(nameof(TestOrchestrator), () => orchestrator));
        actual.Should().BeSameAs(orchestrator);
    }

    [Fact]
    public void AddOrchestrator_Factory_AlreadyAdded()
    {
        Action act = () => RunAddOrchestratorTest(r =>
        {
            r.AddOrchestrator(nameof(TestOrchestrator), () => new TestOrchestrator());
            r.AddOrchestrator(nameof(TestOrchestrator), () => new TestOrchestrator());
        });

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddOrchestrator_Singleton1_Success()
    {
        DurableTaskRegistry registry = new();
        TestOrchestrator orchestrator = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(r => r.AddOrchestrator(nameof(TestOrchestrator), orchestrator));
        actual.Should().BeSameAs(orchestrator);
    }

    [Fact]
    public void AddOrchestrator_Singleton2_Success()
    {
        DurableTaskRegistry registry = new();
        TestOrchestrator orchestrator = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(r => r.AddOrchestrator(orchestrator));
        actual.Should().BeSameAs(orchestrator);
    }

    [Fact]
    public void AddOrchestrator_Type1_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(r => r.AddOrchestrator(nameof(TestOrchestrator), typeof(TestOrchestrator)));
        actual.Should().BeOfType<TestOrchestrator>();
    }

    [Fact]
    public void AddOrchestrator_Type2_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(r => r.AddOrchestrator(typeof(TestOrchestrator)));
        actual.Should().BeOfType<TestOrchestrator>();
    }

    [Fact]
    public void AddOrchestrator_Type1_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddOrchestratorTest(
            r => r.AddOrchestrator(nameof(InvalidOrchestrator), typeof(InvalidOrchestrator)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddOrchestrator_Type2_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddOrchestratorTest(r => r.AddOrchestrator(typeof(InvalidOrchestrator)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddOrchestrator_Generic1_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(
            r => r.AddOrchestrator<TestOrchestrator>(nameof(TestOrchestrator)));
        actual.Should().BeOfType<TestOrchestrator>();
    }

    [Fact]
    public void AddOrchestrator_Generic2_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskOrchestrator actual = RunAddOrchestratorTest(r => r.AddOrchestrator<TestOrchestrator>());
        actual.Should().BeOfType<TestOrchestrator>();
    }

    [Fact]
    public void AddOrchestrator_Generic1_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddOrchestratorTest(
            r => r.AddOrchestrator<InvalidOrchestrator>(nameof(InvalidOrchestrator)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddOrchestrator_Generic2_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddOrchestratorTest(r => r.AddOrchestrator<InvalidOrchestrator>());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddOrchestrator_Func1_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(
                nameof(TestOrchestrator), (TaskOrchestrationContext ctx, string input) => Task.FromResult(input)));

    [Fact]
    public void AddOrchestrator_Func2_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(nameof(TestOrchestrator), (TaskOrchestrationContext ctx, string input) => input));

    [Fact]
    public void AddOrchestrator_Func3_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(
                nameof(TestOrchestrator), (TaskOrchestrationContext ctx, string input) => Task.CompletedTask));

    [Fact]
    public void AddOrchestrator_Func4_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(
                nameof(TestOrchestrator), (TaskOrchestrationContext ctx) => Task.FromResult(string.Empty)));

    [Fact]
    public void AddOrchestrator_Func5_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(nameof(TestOrchestrator), (TaskOrchestrationContext ctx) => Task.CompletedTask));

    [Fact]
    public void AddOrchestrator_Func6_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(nameof(TestOrchestrator), (TaskOrchestrationContext ctx) => string.Empty));

    [Fact]
    public void AddOrchestrator_Action1_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(nameof(TestOrchestrator), (TaskOrchestrationContext ctx, string input) => { }));

    [Fact]
    public void AddOrchestrator_Action2_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(nameof(TestOrchestrator), (TaskOrchestrationContext ctx) => { }));

    static ITaskOrchestrator RunAddOrchestratorTest(Action<DurableTaskRegistry> callback)
    {
        DurableTaskRegistry registry = new();
        callback(registry);
        IDurableTaskFactory factory = registry.BuildFactory();

        bool found = factory.TryCreateOrchestrator(
            nameof(TestOrchestrator), Mock.Of<IServiceProvider>(), out ITaskOrchestrator? actual);
        found.Should().BeTrue();
        actual.Should().NotBeNull();
        return actual!;
    }

    abstract class InvalidOrchestrator: TaskOrchestrator<object, object>
    {
    }

    class TestOrchestrator : TaskOrchestrator<object, object>
    {
        public override Task<object> RunAsync(TaskOrchestrationContext context, object input)
        {
            throw new NotImplementedException();
        }
    }
}
