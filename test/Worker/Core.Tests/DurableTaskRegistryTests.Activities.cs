// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Worker.Tests;

public partial class DurableTaskRegistryTests
{
    [Fact]
    public void AddActivity_DefaultName_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddActivity(default, (IServiceProvider _) => new TestActivity());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");

        act = () => registry.AddActivity(string.Empty, (IServiceProvider _) => new TestActivity());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");

        act = () => registry.AddActivity(null!, (IServiceProvider _) => new TestActivity());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddActivity_Factory_Success()
    {
        TestActivity activity = new();
        ITaskActivity actual = RunAddActivityTest(
            r => r.AddActivity(nameof(TestActivity), (IServiceProvider _) => activity));
        actual.Should().BeSameAs(activity);
    }

    [Fact]
    public void AddActivity_Factory_AlreadyAdded()
    {
        Action act = () => RunAddActivityTest(r =>
        {
            r.AddActivity(nameof(TestActivity), (IServiceProvider _) => new TestActivity());
            r.AddActivity(nameof(TestActivity), (IServiceProvider _) => new TestActivity());
        });

        act.Should().ThrowExactly<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void AddActivity_Singleton1_Success()
    {
        DurableTaskRegistry registry = new();
        TestActivity activity = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity(nameof(TestActivity), activity));
        actual.Should().BeSameAs(activity);
    }

    [Fact]
    public void AddActivity_Singleton2_Success()
    {
        DurableTaskRegistry registry = new();
        TestActivity activity = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity(activity));
        actual.Should().BeSameAs(activity);
    }

    [Fact]
    public void AddActivity_Type1_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity(nameof(TestActivity), typeof(TestActivity)));
        actual.Should().BeOfType<TestActivity>();
    }

    [Fact]
    public void AddActivity_Type2_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity(typeof(TestActivity)));
        actual.Should().BeOfType<TestActivity>();
    }

    [Fact]
    public void AddActivity_Type1_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddActivityTest(r => r.AddActivity(nameof(InvalidActivity), typeof(InvalidActivity)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddActivity_Type2_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddActivityTest(r => r.AddActivity(typeof(InvalidActivity)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddActivity_Generic1_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity<TestActivity>(nameof(TestActivity)));
        actual.Should().BeOfType<TestActivity>();
    }

    [Fact]
    public void AddActivity_Generic2_Success()
    {
        DurableTaskRegistry registry = new();
        ITaskActivity actual = RunAddActivityTest(r => r.AddActivity<TestActivity>());
        actual.Should().BeOfType<TestActivity>();
    }

    [Fact]
    public void AddActivity_Generic1_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddActivityTest(r => r.AddActivity<InvalidActivity>(nameof(InvalidActivity)));
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddActivity_Generic2_Invalid()
    {
        DurableTaskRegistry registry = new();
        Action act = () => RunAddActivityTest(r => r.AddActivity<InvalidActivity>());
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("type");
    }

    [Fact]
    public void AddActivity_Func1_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(
                nameof(TestActivity), (TaskActivityContext ctx, string input) => Task.FromResult(input)));

    [Fact]
    public void AddActivity_Func2_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx, string input) => input));

    [Fact]
    public void AddActivity_Func3_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx, string input) => Task.CompletedTask));

    [Fact]
    public void AddActivity_Func4_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx) => Task.FromResult(string.Empty)));

    [Fact]
    public void AddActivity_Func5_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx) => Task.CompletedTask));

    [Fact]
    public void AddActivity_Func6_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx) => string.Empty));

    [Fact]
    public void AddActivity_Action1_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx, string input) => { }));

    [Fact]
    public void AddActivity_Action2_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(nameof(TestActivity), (TaskActivityContext ctx) => { }));

    static ITaskActivity RunAddActivityTest(Action<DurableTaskRegistry> callback)
    {
        DurableTaskRegistry registry = new();
        callback(registry);
        IDurableTaskFactory factory = registry.BuildFactory();

        bool found = factory.TryCreateActivity(
            nameof(TestActivity), Mock.Of<IServiceProvider>(), out ITaskActivity? actual);
        found.Should().BeTrue();
        actual.Should().NotBeNull();
        return actual!;
    }

    abstract class InvalidActivity : TaskActivity<object, object>
    {
    }

    class TestActivity : TaskActivity<object, object>
    {
        public override Task<object> RunAsync(TaskActivityContext context, object input)
        {
            throw new NotImplementedException();
        }
    }
}
