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

    [Fact]
    public void AddOrchestrator_FuncNoName1_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string, string>(NamedOrchestratorFunc1),
            nameof(NamedOrchestratorFunc1));

    [Fact]
    public void AddOrchestrator_FuncNoName2_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string, string>(NamedOrchestratorFunc2),
            nameof(NamedOrchestratorFunc2));

    [Fact]
    public void AddOrchestrator_FuncNoName3_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string>(NamedOrchestratorFunc3),
            nameof(NamedOrchestratorFunc3));

    [Fact]
    public void AddOrchestrator_FuncNoName4_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string>(NamedOrchestratorFunc4),
            nameof(NamedOrchestratorFunc4));

    [Fact]
    public void AddOrchestrator_FuncNoName5_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(NamedOrchestratorFunc5),
            nameof(NamedOrchestratorFunc5));

    [Fact]
    public void AddOrchestrator_FuncNoName6_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string>(NamedOrchestratorFunc6),
            nameof(NamedOrchestratorFunc6));

    [Fact]
    public void AddOrchestrator_ActionNoName1_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc<string>(NamedOrchestratorAction1),
            nameof(NamedOrchestratorAction1));

    [Fact]
    public void AddOrchestrator_ActionNoName2_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(NamedOrchestratorAction2),
            nameof(NamedOrchestratorAction2));

    [Fact]
    public void AddOrchestrator_FuncNoName_WithAttribute_Success()
        => RunAddOrchestratorTest(
            r => r.AddOrchestratorFunc(AttributedOrchestratorFunc),
            "CustomOrchestratorName");

    [Fact]
    public void AddOrchestrator_FuncNoName_Lambda_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddOrchestratorFunc((TaskOrchestrationContext ctx) => Task.CompletedTask);
        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void AddOrchestrator_FuncNoName_LambdaWithInput_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddOrchestratorFunc<string, string>(
            (TaskOrchestrationContext ctx, string input) => Task.FromResult(input));
        act.Should().ThrowExactly<ArgumentException>();
    }

    static Task<string> NamedOrchestratorFunc1(TaskOrchestrationContext ctx, string input) => Task.FromResult(input);

    static string NamedOrchestratorFunc2(TaskOrchestrationContext ctx, string input) => input;

    static Task NamedOrchestratorFunc3(TaskOrchestrationContext ctx, string input) => Task.CompletedTask;

    static Task<string> NamedOrchestratorFunc4(TaskOrchestrationContext ctx) => Task.FromResult(string.Empty);

    static Task NamedOrchestratorFunc5(TaskOrchestrationContext ctx) => Task.CompletedTask;

    static string NamedOrchestratorFunc6(TaskOrchestrationContext ctx) => string.Empty;

    static void NamedOrchestratorAction1(TaskOrchestrationContext ctx, string input) { }

    static void NamedOrchestratorAction2(TaskOrchestrationContext ctx) { }

    [DurableTask("CustomOrchestratorName")]
    static Task AttributedOrchestratorFunc(TaskOrchestrationContext ctx) => Task.CompletedTask;

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

    static ITaskOrchestrator RunAddOrchestratorTest(Action<DurableTaskRegistry> callback, string expectedName)
    {
        DurableTaskRegistry registry = new();
        callback(registry);
        IDurableTaskFactory factory = registry.BuildFactory();

        bool found = factory.TryCreateOrchestrator(
            expectedName, Mock.Of<IServiceProvider>(), out ITaskOrchestrator? actual);
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
