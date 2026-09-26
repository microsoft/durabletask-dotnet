// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Reflection;
using Azure.Core;
using Azure.Identity;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
#if SCHEDULER_WORKER
using Microsoft.DurableTask.Worker.AzureManaged;
using SchedulerBuilder = Microsoft.DurableTask.Worker.IDurableTaskWorkerBuilder;
using SchedulerOptions = Microsoft.DurableTask.DurableTaskSchedulerWorkerOptions;
using GrpcOptions = Microsoft.DurableTask.Worker.Grpc.GrpcDurableTaskWorkerOptions;
#else
using Microsoft.DurableTask.Client.AzureManaged;
using SchedulerBuilder = Microsoft.DurableTask.Client.IDurableTaskClientBuilder;
using SchedulerOptions = Microsoft.DurableTask.DurableTaskSchedulerClientOptions;
using GrpcOptions = Microsoft.DurableTask.Client.Grpc.GrpcDurableTaskClientOptions;
#endif

namespace Microsoft.DurableTask.AzureManaged.Tests;

[CollectionDefinition("Scheduler authentication environment", DisableParallelization = true)]
public class SchedulerAuthenticationEnvironmentCollection;

[Collection("Scheduler authentication environment")]
public class SchedulerAuthenticationTests(SchedulerAuthenticationServer server) : IClassFixture<SchedulerAuthenticationServer>
{
    public static IEnumerable<object?[]> ResourceIds()
    {
        (string? Region, string? Resource, string Expected)[] cases =
        [
            (null, null, "https://durabletask.io"),
            ("", null, "https://durabletask.io"),
            ("westus2", "", "https://durabletask.io"),
            ("chinaeast2", null, "https://durabletask.io"),
            ("notusgov", null, "https://durabletask.io"),
            ("notusdod", null, "https://durabletask.io"),
            ("westusgov", null, "https://durabletask.io"),
            (" usgovvirginia", null, "https://durabletask.io"),
            ("usgovvirginia", null, "https://durabletask.azure.us"),
            ("USGOVARIZONA", "", "https://durabletask.azure.us"),
            ("UsGovTexas", null, "https://durabletask.azure.us"),
            ("usdodcentral", null, "https://durabletask.azure.us"),
            ("USDODEAST", "", "https://durabletask.azure.us"),
            ("UsDodCentral", null, "https://durabletask.azure.us"),
            ("usgov", null, "https://durabletask.azure.us"),
            ("usdod", null, "https://durabletask.azure.us"),
            ("usgovvirginia", "https://durabletask.io", "https://durabletask.io"),
            ("usdodcentral", "https://durabletask.io", "https://durabletask.io"),
            ("westus2", "https://durabletask.azure.us", "https://durabletask.azure.us"),
            (null, "https://durabletask.azure.us/", "https://durabletask.azure.us"),
            (null, "https://durabletask.azure.us//.DEFAULT//", "https://durabletask.azure.us"),
            (null, " \thttps://durabletask.azure.us/.default/ \t", "https://durabletask.azure.us"),
            ("usgovvirginia", "api://CustomAudience/resource/.DEFAULT/", "api://CustomAudience/resource"),
            ("westus2", "api://custom/.default/.default", "api://custom/.default"),
        ];

        foreach (var item in cases)
        {
            foreach (bool fromConnectionString in new[] { false, true })
            {
                yield return [item.Region, item.Resource, item.Expected, fromConnectionString];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ResourceIds))]
    public async Task ResourceId_RequestsNormalizedScopeAndPreservesItAcrossRefreshAsync(
        string? region, string? resourceId, string expectedResource, bool fromConnectionString)
    {
        // Arrange
        using SchedulerEnvironmentVariable regionVariable = new("REGION_NAME", region);
        RecordingSchedulerCredential credential = new(expireFirstToken: true);
        SchedulerOptions options = fromConnectionString
            ? SchedulerOptions.FromConnectionString(this.ConnectionString(resourceId))
            : new SchedulerOptions { EndpointAddress = server.Endpoint, TaskHubName = "testhub", ResourceId = resourceId };
        options.Credential = credential;
        options.AllowInsecureCredentials = true;
        using GrpcChannel channel = options.CreateChannel();
        Assert.Empty(credential.Scopes);

        // Act
        Environment.SetEnvironmentVariable("REGION_NAME", "changed-after-channel-creation");
        await server.CallAsync(channel);
        await server.CallAsync(channel);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.CallAsync(channel)));

        // Assert
        Assert.Equal(expectedResource, options.ResourceId);
        Assert.Equal([expectedResource + "/.default", expectedResource + "/.default"], credential.Scopes);
        Assert.Equal(server.Endpoint, options.EndpointAddress);
        Assert.Same(credential, options.Credential);
    }

    [Theory]
    [InlineData(" \t ")]
    [InlineData("///")]
    [InlineData("/.default")]
    [InlineData("/.DEFAULT///")]
    public void ResourceId_RejectsEmptyAfterNormalizationWithoutCredentials(string resourceId)
    {
        // Arrange
        SchedulerOptions options = new();

        // Act
        ArgumentException direct = Assert.Throws<ArgumentException>(() => options.ResourceId = resourceId);
        ArgumentException connection = Assert.Throws<ArgumentException>(
            () => SchedulerOptions.FromConnectionString(this.ConnectionString(resourceId)));

        // Assert
        Assert.Contains("ResourceId", direct.Message);
        Assert.Contains("token audience URI", direct.Message);
        Assert.Contains("ResourceId", connection.Message);
        Assert.Null(options.Credential);
    }

    [Fact]
    public async Task Defaults_AreResolvedPerOptionsInstanceAndNotFromEndpointAsync()
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "usgovvirginia");
        SchedulerOptions government = new() { EndpointAddress = server.Endpoint, TaskHubName = "testhub" };
        SchedulerOptions governmentConnection = SchedulerOptions.FromConnectionString(
            $"Endpoint={server.Endpoint};TaskHub=testhub;Authentication=None");
        Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
        SchedulerOptions publicOptions = new() { EndpointAddress = server.Endpoint, TaskHubName = "testhub" };
        SchedulerOptions governmentEndpoint = new()
        {
            EndpointAddress = "https://example.usgovvirginia.durabletask.azure.us",
        };
        Environment.SetEnvironmentVariable("REGION_NAME", "usdodcentral");
        publicOptions.ResourceId = null;
        government.ResourceId = "";
        RecordingSchedulerCredential credential = new();

        // Act
        foreach (SchedulerOptions options in new[] { government, governmentConnection, publicOptions })
        {
            options.Credential = credential;
            options.AllowInsecureCredentials = true;
            using GrpcChannel channel = options.CreateChannel();
            await server.CallAsync(channel);
        }

        // Assert
        Assert.Equal(
            ["https://durabletask.azure.us/.default", "https://durabletask.azure.us/.default", "https://durabletask.io/.default"],
            credential.Scopes);
        Assert.Equal("https://durabletask.io", governmentEndpoint.ResourceId);
        Assert.Equal("https://example.usgovvirginia.durabletask.azure.us", governmentEndpoint.EndpointAddress);
    }

    [Fact]
    public async Task ResourceId_RefreshOnPreservesSelectedScopeAsync()
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "usgovvirginia");
        RecordingSchedulerCredential credential = new(refreshFirstToken: true);
        SchedulerOptions options = new()
        {
            EndpointAddress = server.Endpoint,
            TaskHubName = "testhub",
            Credential = credential,
            AllowInsecureCredentials = true,
        };
        using GrpcChannel channel = options.CreateChannel();

        // Act
        await server.CallAsync(channel);
        Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
        await server.CallAsync(channel);

        // Assert
        Assert.Equal(Enumerable.Repeat("https://durabletask.azure.us/.default", 2), credential.Scopes);
    }

    [Theory]
    [CombinatorialData]
    public async Task Builders_PreserveAudienceAcrossOptionsCopyAndChannelRecreationAsync(
        [CombinatorialValues("options", "endpoint", "connectionString")] string path,
        [CombinatorialValues(null, "", "https://durabletask.io", "api://Custom/.default/.DEFAULT/")] string? resourceId,
        bool callbackOverride)
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "UsDodCentral");
        RecordingSchedulerCredential credential = new(expireFirstToken: true);
        ServiceCollection services = new();
        Mock<SchedulerBuilder> builder = new();
        builder.SetupGet(b => b.Services).Returns(services);
        builder.SetupGet(b => b.Name).Returns("named");
        Action<SchedulerOptions> configure = options =>
        {
            options.Credential = credential;
            options.AllowInsecureCredentials = true;
            if (callbackOverride)
            {
                options.ResourceId = "api://Override/Resource/.DEFAULT/";
            }
        };
        switch (path)
        {
            case "options":
                builder.Object.UseDurableTaskScheduler(options =>
                {
                    options.EndpointAddress = server.Endpoint;
                    options.TaskHubName = "testhub";
                    options.ResourceId = resourceId;
                    configure(options);
                });
                break;
            case "endpoint":
                builder.Object.UseDurableTaskScheduler(server.Endpoint, "testhub", credential, options =>
                {
                    options.ResourceId = resourceId;
                    configure(options);
                });
                break;
            default:
                builder.Object.UseDurableTaskScheduler(this.ConnectionString(resourceId), configure);
                // Connection-string options are captured at registration time, not DI resolution.
                Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
                break;
        }

        await using ServiceProvider provider = services.BuildServiceProvider();
        GrpcOptions grpcOptions = provider.GetRequiredService<IOptionsMonitor<GrpcOptions>>().Get("named");
        GrpcChannel original = Assert.IsType<GrpcChannel>(grpcOptions.Channel);
        Assert.Empty(credential.Scopes);
        string expected = callbackOverride ? "api://Override/Resource"
            : string.IsNullOrEmpty(resourceId) ? "https://durabletask.azure.us"
            : resourceId == "https://durabletask.io" ? resourceId : "api://Custom/.default";

        // Act
        await server.CallAsync(original);
        Environment.SetEnvironmentVariable("REGION_NAME", "westus2");
        await server.CallAsync(original);
        Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> recreate = GetChannelRecreator(grpcOptions);
        GrpcChannel replacement = await recreate(original, CancellationToken.None);
        await server.CallAsync(replacement);

        // Assert
        Assert.NotSame(original, replacement);
        Assert.Equal(Enumerable.Repeat(expected + "/.default", 3), credential.Scopes);
        Assert.Equal(expected, provider.GetRequiredService<IOptionsMonitor<SchedulerOptions>>().Get("named").ResourceId);
    }

    [Theory]
    [InlineData("DefaultAzure")]
    [InlineData("ManagedIdentity")]
    [InlineData("WorkloadIdentity")]
    [InlineData("Environment")]
    [InlineData("AzureCLI")]
    [InlineData("AzurePowerShell")]
    [InlineData("VisualStudio")]
    [InlineData("VisualStudioCode")]
    [InlineData("InteractiveBrowser")]
    [InlineData("None")]
    public async Task ConnectionString_ResourceIdAppliesToEveryAuthenticationTypeAsync(string authentication)
    {
        // Arrange
        using SchedulerEnvironmentVariable tokenFile = new("AZURE_FEDERATED_TOKEN_FILE", "unused-test-token-file");
        SchedulerOptions options = SchedulerOptions.FromConnectionString(
            $"Endpoint={server.Endpoint};TaskHub=testhub;Authentication={authentication};" +
            "ClientID=11111111-1111-1111-1111-111111111111;TenantId=22222222-2222-2222-2222-222222222222;" +
            "resourceid=\" api://CustomAudience/.DEFAULT/ \";AuthorityHost=https://login.microsoftonline.us/");
        RecordingSchedulerCredential credential = new();
        bool anonymous = options.Credential is null;
        options.Credential = credential;
        options.AllowInsecureCredentials = true;

        // Act
        using GrpcChannel channel = options.CreateChannel();
        await server.CallAsync(channel);

        // Assert
        Assert.Equal(authentication == "None", anonymous);
        Assert.Equal(["api://CustomAudience/.default"], credential.Scopes);
    }

    [Theory]
    [InlineData("https://durabletask.azure.us")]
    [InlineData(null)]
    public async Task AnonymousChannel_DoesNotAttachAuthorizationAsync(string? resourceId)
    {
        // Arrange
        using SchedulerEnvironmentVariable region = new("REGION_NAME", "usgovvirginia");
        SchedulerOptions options = SchedulerOptions.FromConnectionString(this.ConnectionString(resourceId));
        using GrpcChannel channel = options.CreateChannel();

        // Act
        byte[] response = await server.CallAsync(channel);

        // Assert
        Assert.Empty(response);
        Assert.Null(options.Credential);
    }

    static Func<GrpcChannel, CancellationToken, Task<GrpcChannel>> GetChannelRecreator(GrpcOptions options)
    {
        object internalOptions = typeof(GrpcOptions)
            .GetProperty("Internal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(options)!;
        return Assert.IsType<Func<GrpcChannel, CancellationToken, Task<GrpcChannel>>>(
            internalOptions.GetType().GetProperty("ChannelRecreator")!.GetValue(internalOptions));
    }

    string ConnectionString(string? resourceId) =>
        $"Endpoint={server.Endpoint};TaskHub=testhub;Authentication=None;resourceid=\"{resourceId}\"";
}

public sealed class RecordingSchedulerCredential(bool expireFirstToken = false, bool refreshFirstToken = false) : TokenCredential
{
    readonly ConcurrentQueue<string> scopes = new();

    public string[] Scopes => this.scopes.ToArray();

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Scheduler authentication must remain asynchronous.");

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        this.scopes.Enqueue(Assert.Single(requestContext.Scopes));
        return ValueTask.FromResult(new AccessToken(
            "recorded-token",
            expireFirstToken && this.scopes.Count == 1 ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1),
            refreshFirstToken && this.scopes.Count == 1 ? DateTimeOffset.UtcNow.AddMinutes(-1) : null));
    }
}

public sealed class SchedulerEnvironmentVariable : IDisposable
{
    readonly string name;
    readonly string? originalValue;

    public SchedulerEnvironmentVariable(string name, string? value)
    {
        this.name = name;
        this.originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(this.name, this.originalValue);
}

public sealed class SchedulerAuthenticationServer : IAsyncLifetime
{
    static readonly Marshaller<byte[]> Marshaller = Marshallers.Create<byte[]>(value => value, value => value);
    static readonly Method<byte[], byte[]> Call = new(MethodType.Unary, "test", "Call", Marshaller, Marshaller);
    readonly Server server;

    public SchedulerAuthenticationServer()
    {
        this.server = new Server
        {
            Ports = { new ServerPort("127.0.0.1", 0, ServerCredentials.Insecure) },
            Services =
            {
                ServerServiceDefinition.CreateBuilder()
                    .AddMethod(Call, (byte[] _, ServerCallContext context) =>
                    {
                        Assert.Equal("testhub", Assert.Single(context.RequestHeaders, h => h.Key == "taskhub").Value);
                        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(context.RequestHeaders.GetValue("authorization") ?? ""));
                    })
                    .AddMethod(SandboxMethod("DeclareSandboxWorkerProfile", MethodType.Unary), this.ManageSandboxAsync)
                    .AddMethod(SandboxMethod("RemoveSandboxWorkerProfile", MethodType.Unary), this.ManageSandboxAsync)
                    .AddMethod(
                        SandboxMethod("ConnectSandboxActivityWorker", MethodType.ClientStreaming),
                        (IAsyncStreamReader<byte[]> requests, ServerCallContext context) =>
                            this.RegistrationHandler!(requests, context))
                    .Build(),
            },
        };
    }

    public string Endpoint => $"http://127.0.0.1:{this.server.Ports.Single().BoundPort}";

    public ConcurrentQueue<Metadata> ManagementHeaders { get; } = new();

    public Func<IAsyncStreamReader<byte[]>, ServerCallContext, Task<byte[]>>? RegistrationHandler { get; set; }

    public Task InitializeAsync()
    {
        this.server.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => this.server.ShutdownAsync();

    public async Task<byte[]> CallAsync(GrpcChannel channel)
    {
        using AsyncUnaryCall<byte[]> call = channel.CreateCallInvoker().AsyncUnaryCall(
            Call, null, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(10)), []);
        return await call.ResponseAsync;
    }

    static Method<byte[], byte[]> SandboxMethod(string name, MethodType type) =>
        new(type, "microsoft.durabletask.sandboxes.SandboxActivities", name, Marshaller, Marshaller);

    Task<byte[]> ManageSandboxAsync(byte[] request, ServerCallContext context)
    {
        this.ManagementHeaders.Enqueue(context.RequestHeaders);
        return Task.FromResult(Array.Empty<byte>());
    }
}
