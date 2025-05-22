// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Company.Function; // same namespace as the Azure Functions app

using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Dapr.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

public class SampleUnitTests
{
    [Fact]
    public async Task OrchestrationReturnsMultipleGreetings()
    {
        // create mock orchestration context, and mock ILogger.
        Mock<TaskOrchestrationContext> contextMock = new();

        // a simple ILogger that captures emitted logs in a list
        TestLogger logger = new();

        // The DurableTaskClient CreateReplaySafeLogger API obtains a logger from a protected LoggerFactory property, we mock it here
        Mock<ILoggerFactory> loggerFactoryMock = new();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(logger);
        contextMock.Protected().Setup<ILoggerFactory>("LoggerFactory").Returns(loggerFactoryMock.Object);
        contextMock.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>()))
               .Returns((string categoryName) => loggerFactoryMock.Object.CreateLogger(categoryName));


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
        Mock<FunctionContext> contextMock = new();

        // a simple ILogger that captures emitted logs in a list
        TestLogger logger = new();

        // Mock ILogger service, needed since an ILogger is created in the client via <FunctionContext>.GetLogger(...);
        Mock<ILoggerFactory> loggerFactoryMock = new();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(logger);
        Mock<IServiceProvider> instanceServicesMock = new();
        instanceServicesMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        // register mock'ed DI services
        var instanceServices = instanceServicesMock.Object;
        contextMock.Setup(x => x.InstanceServices).Returns(instanceServices);

        var context = contextMock.Object;

        string output = AzureFunctionsApp.SayHello("Tokyo", context);

        // Assert expected logs are emitted
        var capturedLogs = logger.CapturedLogs;
        Assert.Contains(capturedLogs, log => log.Contains("Saying hello to Tokyo."));

        // assert expected outputs  
        Assert.Equal("Hello Tokyo!", output);
    }

    [Fact]
    public async Task ClientReturnsUrls()
    {
        // orchestrator instanceID ID we expect to generated
        var instanceId = "myInstanceId";

        // we need to mock the FunctionContext and provide it with two mocked services needed by the client
        // (1) an ILogger service
        // (2) an ObjectSerializer service,
        Mock<FunctionContext> contextMock = new();

        // a simple ILogger that captures emitted logs in a list
        TestLogger logger = new();

        // Mock ILogger service, needed since an ILogger is created in the client via <FunctionContext>.GetLogger(...);
        Mock<ILoggerFactory> loggerFactoryMock = new();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(logger);
        Mock<IServiceProvider> instanceServicesMock = new();
        instanceServicesMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        // mock JsonObjectSerializer service, used during HTTP response serialization
        ObjectSerializer serializer = new JsonObjectSerializer();
        IOptions<WorkerOptions> options = new OptionsWrapper<WorkerOptions>(new WorkerOptions());
        options.Value.Serializer = serializer;
        instanceServicesMock.Setup(x => x.GetService(typeof(IOptions<WorkerOptions>))).Returns(options);

        // register mock'ed DI services
        var instanceServices = instanceServicesMock.Object;
        contextMock.Setup(x => x.InstanceServices).Returns(instanceServices);

        // instantiate worker context
        var context = contextMock.Object;

        // Initialize mock'ed DurableTaskClient with the ability to start orchestrations
        Mock<DurableTaskClient> clientMock = new("test client");
        clientMock.Setup(x => x.ScheduleNewOrchestrationInstanceAsync(nameof(AzureFunctionsApp),
            It.IsAny<object>(),
            It.IsAny<StartOrchestrationOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(instanceId);
        var client = clientMock.Object;

        // Create dummy request object
        TestRequestData request = new(context);

        // Invoke the function
        var output = await AzureFunctionsApp.HttpStart(request, client, context);

        // Assert expected logs are emitted
        var capturedLogs = logger.CapturedLogs;
        Assert.Contains(capturedLogs, log => log.Contains($"Started orchestration with ID = '{instanceId}'"));

        // deserialize http output
        output.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(output.Body, Encoding.UTF8);
        string content = reader.ReadToEnd();
        Dictionary<string, string>? keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, string>>(content);

        // Validate format of response URLs
        Assert.NotNull(keyValuePairs);
        Assert.Contains(keyValuePairs, kvp => kvp.Key == "Id" && kvp.Value == instanceId);
        Assert.Contains(keyValuePairs, kvp => kvp.Key == "PurgeHistoryDeleteUri" && kvp.Value == $"http://localhost:8888/runtime/webhooks/durabletask/instances/{instanceId}");
        Assert.Contains(keyValuePairs, kvp => kvp.Key == "SendEventPostUri" && kvp.Value == $"http://localhost:8888/runtime/webhooks/durabletask/instances/{instanceId}/raiseEvent/{{eventName}}");
        Assert.Contains(keyValuePairs, kvp => kvp.Key == "StatusQueryGetUri" && kvp.Value == $"http://localhost:8888/runtime/webhooks/durabletask/instances/{instanceId}");

    }

    // naive implementation of HttpRequestData for testing purposes
    public class TestRequestData : HttpRequestData
    {
        readonly FunctionContext context;

        public TestRequestData(FunctionContext functionContext) : base(functionContext)
        {
            this.context = functionContext;
        }

        public override Stream Body => new MemoryStream();

        public override HttpHeadersCollection Headers => new();

        public override IReadOnlyCollection<IHttpCookie> Cookies => new List<IHttpCookie>();

        public override Uri Url => new("http://localhost:8888/myUrl");

        public override IEnumerable<ClaimsIdentity> Identities => Enumerable.Empty<ClaimsIdentity>();

        public override string Method => "POST";

        public override HttpResponseData CreateResponse()
        {
            return new TestResponse(this.context);
        }
    }

    // naive implementation of HttpResponseData for testing purposes, creating by TestRequestData's `CreateResponse` method
    public class TestResponse : HttpResponseData
    {
        public TestResponse(FunctionContext functionContext) : base(functionContext)
        {
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();

        public override HttpCookies Cookies => throw new NotImplementedException();

        public override Stream Body { get; set; } = new MemoryStream();
    }

    public class TestLogger : ILogger
    {
        // list of all logs emitted, for validation
        public IList<string> CapturedLogs {get; set;} = new List<string>();

        public IDisposable BeginScope<TState>(TState state) => Mock.Of<IDisposable>();

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string formattedLog = formatter(state, exception);
            this.CapturedLogs.Add(formattedLog);
        }

    }
}