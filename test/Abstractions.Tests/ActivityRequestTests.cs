// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace Microsoft.DurableTask.Tests;

public class ActivityRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public void Create_Success(int? input)
    {
        TaskName name = "Test";
        IActivityRequest request = ActivityRequest.Create(name, input);

        request.GetInput().Should().Be(input);
        request.GetTaskName().Should().Be(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("input")]
    public void Create_OfT_Success(string input)
    {
        TaskName name = "Test";
        IActivityRequest<int> request = ActivityRequest.Create<int>(name, input);

        request.GetInput().Should().Be(input);
        request.GetTaskName().Should().Be(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("input")]
    public async Task RunRequest_Success(string input)
    {
        TaskName name = "Test";
        IActivityRequest request = ActivityRequest.Create(name, input);
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        context.Setup(m => m.CallActivityAsync(name, input, null)).Returns(Task.CompletedTask);

        await context.Object.RunAsync(request);

        context.Verify(m => m.CallActivityAsync(name, input, null), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("input")]
    public async Task RunRequest_OfT_Success(string input)
    {
        TaskName name = "Test";
        IActivityRequest<int> request = ActivityRequest.Create<int>(name, input);
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        context.Setup(m => m.CallActivityAsync<int>(name, input, null)).Returns(Task.FromResult(0));

        await context.Object.RunAsync(request);

        context.Verify(m => m.CallActivityAsync<int>(name, input, null), Times.Once);
    }

    [Fact]
    public async Task RunRequest2_Success()
    {
        IActivityRequest request = new DirectRequest();
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        context.Setup(m => m.CallActivityAsync(DirectRequest.Name, request, null)).Returns(Task.CompletedTask);

        await context.Object.RunAsync(request);

        context.Verify(m => m.CallActivityAsync(DirectRequest.Name, request, null), Times.Once);
    }

    [Fact]
    public async Task RunRequest2_OfT_Success()
    {
        TaskName name = "Test";
        IActivityRequest<int> request = new DirectRequest2();
        Mock<TaskOrchestrationContext> context = new(MockBehavior.Strict);
        context.Setup(m => m.CallActivityAsync<int>(DirectRequest.Name, request, null))
            .Returns(Task.FromResult(0));

        await context.Object.RunAsync(request);

        context.Verify(m => m.CallActivityAsync<int>(DirectRequest.Name, request, null), Times.Once);
    }

    class DirectRequest : IActivityRequest
    {
        public static readonly TaskName Name = "DirectRequest";

        public TaskName GetTaskName() => Name;
    }

    class DirectRequest2 : IActivityRequest<int>
    {
        public TaskName GetTaskName() => DirectRequest.Name;
    }
}
