// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Company.Function; // same namespace as the Azure Functions app

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Moq;

public class SampleUnitTests
{
    [Fact]
    public async Task OrchestrationReturnsMultipleGreetings()
    {
        // create mock orchestration context, and mock ILogger.
        var contextMock = new Mock<TaskOrchestrationContext>();

        // mock activity results
        // In Moq, optional arguments need to be specified as well. We specify them with It.IsAny<T>(), where T is the type of the optional argument
        contextMock.Setup(x => x.CallActivityAsync<string>(nameof(AzureFunctionsApp.SayHello), "Tokyo", It.IsAny<TaskOptions>()))
            .ReturnsAsync("Hello Tokyo!");
        contextMock.Setup(x => x.CallActivityAsync<string>(nameof(AzureFunctionsApp.SayHello), "Seattle", It.IsAny<TaskOptions>()))
            .ReturnsAsync("Hello Seattle!");
        contextMock.Setup(x => x.CallActivityAsync<string>(nameof(AzureFunctionsApp.SayHello), "London", It.IsAny<TaskOptions>()))
            .ReturnsAsync("Hello London!");

        // execute the orchestrator
        var contextObj = contextMock.Object;
        List<string> outputs = await AzureFunctionsApp.RunOrchestrator(contextObj);

        // assert expected outputs
        Assert.Equal(3, outputs.Count);
        Assert.Equal("Hello Tokyo!", outputs[0]);
        Assert.Equal("Hello Seattle!", outputs[1]);
        Assert.Equal("Hello London!", outputs[2]);
    }

    [Fact]
    public void ActivityReturnsGreeting()
    {
        var contextMock = new Mock<FunctionContext>();
        var context = contextMock.Object;

        string output = AzureFunctionsApp.SayHello("Tokyo", context);

        // assert expected outputs  
        Assert.Equal("Hello Tokyo!", output);
    }
}