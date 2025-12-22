// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.Tests;

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

    [Fact]
    public void AddActivity_FuncNoName1_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string, string>(NamedActivityFunc1),
            nameof(NamedActivityFunc1));

    [Fact]
    public void AddActivity_FuncNoName2_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string, string>(NamedActivityFunc2),
            nameof(NamedActivityFunc2));

    [Fact]
    public void AddActivity_FuncNoName3_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string>(NamedActivityFunc3),
            nameof(NamedActivityFunc3));

    [Fact]
    public void AddActivity_FuncNoName4_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string>(NamedActivityFunc4),
            nameof(NamedActivityFunc4));

    [Fact]
    public void AddActivity_FuncNoName5_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(NamedActivityFunc5),
            nameof(NamedActivityFunc5));

    [Fact]
    public void AddActivity_FuncNoName6_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string>(NamedActivityFunc6),
            nameof(NamedActivityFunc6));

    [Fact]
    public void AddActivity_ActionNoName1_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc<string>(NamedActivityAction1),
            nameof(NamedActivityAction1));

    [Fact]
    public void AddActivity_ActionNoName2_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(NamedActivityAction2),
            nameof(NamedActivityAction2));

    [Fact]
    public void AddActivity_FuncNoName_WithAttribute_Success()
        => RunAddActivityTest(
            r => r.AddActivityFunc(AttributedActivityFunc),
            "CustomActivityName");

    [Fact]
    public void AddActivity_FuncNoName_Lambda_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddActivityFunc((TaskActivityContext ctx) => Task.CompletedTask);
        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void AddActivity_FuncNoName_LambdaWithInput_Throws()
    {
        DurableTaskRegistry registry = new();
        Action act = () => registry.AddActivityFunc<string, string>(
            (TaskActivityContext ctx, string input) => Task.FromResult(input));
        act.Should().ThrowExactly<ArgumentException>();
    }

    static Task<string> NamedActivityFunc1(TaskActivityContext ctx, string input) => Task.FromResult(input);

    static string NamedActivityFunc2(TaskActivityContext ctx, string input) => input;

    static Task NamedActivityFunc3(TaskActivityContext ctx, string input) => Task.CompletedTask;

    static Task<string> NamedActivityFunc4(TaskActivityContext ctx) => Task.FromResult(string.Empty);

    static Task NamedActivityFunc5(TaskActivityContext ctx) => Task.CompletedTask;

    static string NamedActivityFunc6(TaskActivityContext ctx) => string.Empty;

    static void NamedActivityAction1(TaskActivityContext ctx, string input) { }

    static void NamedActivityAction2(TaskActivityContext ctx) { }

    [DurableTask("CustomActivityName")]
    static Task AttributedActivityFunc(TaskActivityContext ctx) => Task.CompletedTask;

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

    static ITaskActivity RunAddActivityTest(Action<DurableTaskRegistry> callback, string expectedName)
    {
        DurableTaskRegistry registry = new();
        callback(registry);
        IDurableTaskFactory factory = registry.BuildFactory();

        bool found = factory.TryCreateActivity(
            expectedName, Mock.Of<IServiceProvider>(), out ITaskActivity? actual);
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
