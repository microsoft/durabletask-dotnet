// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using FluentAssertions;
using Grpc.Core;
using Microsoft.DurableTask.AzureManaged.Internal;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Protobuf.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.DurableTask.Client.AzureManaged.Tests;

public class SandboxActivitiesClientTests
{
    const string TaskHub = "testhub";

    [Fact]
    public void OnDemandSandboxDeclarationContract_DoesNotExposeRemovedOptions()
    {
        typeof(SandboxWorkerProfileOptions).GetProperty("LaunchCommand").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("DeclarationRetryMaxAttempts").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("DeclarationRetryDelay").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "HeartbeatInterval",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("WakeupPort").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "WorkerRegistrationRetryInitialDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "WorkerRegistrationRetryMaxDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "Mode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxActivityDeclaration).GetProperty("LaunchCommand").Should().BeNull();
    }

    [Fact]
    public void OnDemandSandboxDeclarationContract_ExposesProfileAddActivityOnly()
    {
        // Arrange
        Type optionsType = typeof(SandboxWorkerProfileOptions);
        Type? activityAttributeType = typeof(SandboxWorkerProfileOptions).Assembly.GetType(
            "Microsoft.DurableTask.Client.AzureManaged.SandboxesActivityAttribute");

        // Act/Assert
        optionsType.GetProperty("ActivityNames").Should().BeNull();
        optionsType.GetMethod("AddActivity", [typeof(string)]).Should().NotBeNull();
        optionsType.GetMethods().Should().Contain(method =>
            method.Name == "AddActivity" && method.IsGenericMethodDefinition);
        activityAttributeType.Should().BeNull();
    }

    [Theory]
    [InlineData("500m", "1024Mi")]
    [InlineData("0.5", "1Gi")]
    [InlineData("2", "2048")]
    public void SandboxActivityDeclarationBuilder_BuildDeclaration_AcceptsAdcResourceQuantities(
        string cpu,
        string memory)
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateDeclarationOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        SandboxActivityDeclaration declaration = SandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        declaration.Resources.Cpu.Should().Be(cpu);
        declaration.Resources.Memory.Should().Be(memory);
    }

    [Theory]
    [InlineData("0", "1024Mi", "CPU")]
    [InlineData("0m", "1024Mi", "CPU")]
    [InlineData("500.5m", "1024Mi", "CPU")]
    [InlineData("500Mi", "1024Mi", "CPU")]
    [InlineData("500m", "0", "memory")]
    [InlineData("500m", "0Mi", "memory")]
    [InlineData("500m", "500m", "memory")]
    public void SandboxActivityDeclarationBuilder_BuildDeclaration_RejectsInvalidAdcResourceQuantities(
        string cpu,
        string memory,
        string expectedMessage)
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateDeclarationOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        Action action = () => SandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void SandboxActivityDeclarationBuilder_ResolveActivityNames_DeduplicatesCaseInsensitively()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateDeclarationOptions();
        options.AddActivity(" RemoteHello ");
        options.AddActivity("remotehello");
        options.AddActivity("Other");

        // Act
        string[] activityNames = SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames);

        // Assert
        activityNames.Should().Equal("RemoteHello", "Other");
    }

    [Fact]
    public void SandboxActivityDeclarationBuilder_BuildDeclaration_RequiresSchedulerManagedIdentityClientId()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateDeclarationOptions();
        options.SchedulerManagedIdentityClientId = " ";

        // Act
        Action action = () => SandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID*");
    }

    [Fact]
    public void SandboxActivityDeclarationBuilder_BuildDeclaration_RequiresImagePullManagedIdentityClientId()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateDeclarationOptions();
        options.ImagePullManagedIdentityClientId = " ";

        // Act
        Action action = () => SandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID ADC uses to pull the worker image*");
    }

    [Fact]
    public void SandboxActivityDeclarationProvider_ResolveDeclarations_UsesWorkerProfileConfigure()
    {
        // Arrange
        using EnvironmentVariableScope image = new("DTS_ON_DEMAND_SANDBOX_ACTIVITY_IMAGE", "example.com/not-used:latest");
        using EnvironmentVariableScope cpu = new("DTS_ON_DEMAND_SANDBOX_CPU", "2000m");
        using EnvironmentVariableScope memory = new("DTS_ON_DEMAND_SANDBOX_MEMORY", "4096Mi");
        using EnvironmentVariableScope maxActivities = new("DTS_SANDBOX_MAX_ACTIVITIES", "99");

        SandboxActivityDeclarationProvider provider = new();

        // Act
        SandboxWorkerProfileOptions options = provider.ResolveDeclarations(TaskHub)
            .Single(options => options.WorkerProfileId == "annotated-profile");
        SandboxActivityDeclaration declaration = SandboxActivityDeclarationBuilder.BuildDeclaration(
            options,
            SandboxActivityDeclarationBuilder.ResolveActivityNames(options.ActivityNames));

        // Assert
        declaration.WorkerProfileId.Should().Be("annotated-profile");
        declaration.ActivityNames.Should().Equal("ConfiguredRemoteHello");
        declaration.Image.ImageRef.Should().Be("example.com/repo/annotated-worker:latest");
        declaration.Image.ManagedIdentityClientId.Should().Be("image-pull-client-id");
        declaration.SchedulerManagedIdentityClientId.Should().Be("scheduler-client-id");
        declaration.Resources.Cpu.Should().Be("500m");
        declaration.Resources.Memory.Should().Be("1024Mi");
        declaration.MaxConcurrentActivities.Should().Be(4);
        declaration.EnvironmentVariables.Should().ContainKey("CUSTOM_ENV").WhoseValue.Should().Be("configured-value");
        declaration.Image.Entrypoint.Should().BeEmpty();
        declaration.Image.Cmd.Should().BeEmpty();
    }

    [Fact]
    public void SandboxActivityDeclarationProvider_ValidateProfileType_RequiresProfileInterface()
    {
        // Arrange
        // Act
        Action action = () => SandboxActivityDeclarationProvider.ValidateProfileType(typeof(ProfileWithoutInterface));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ISandboxWorkerProfile)}*");
    }

    [Fact]
    public void SandboxActivityDeclarationProvider_ResolveDeclarations_DetectsCaseInsensitiveActivityOwnership()
    {
        // Arrange
        using EnvironmentVariableScope enableDuplicateCaseProfiles = new(
            "DTS_TEST_ENABLE_CASE_DUPLICATE_SANDBOX_PROFILES",
            "true");
        SandboxActivityDeclarationProvider provider = new();

        // Act
        Action action = () => provider.ResolveDeclarations(TaskHub);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("CaseActivity", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("case-profile-a", StringComparison.Ordinal)
                && ex.Message.Contains("case-profile-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddDurableTaskSchedulerSandboxActivitiesClient_UsesConfiguredDurableTaskClientInvoker()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        ServiceCollection services = new();
        services.AddOptions<DurableTaskSchedulerClientOptions>(Options.DefaultName)
            .Configure(options => options.TaskHubName = "client-test-taskhub");
        services.AddOptions<GrpcDurableTaskClientOptions>(Options.DefaultName)
            .Configure(options => options.CallInvoker = callInvoker);
        services.AddDurableTaskSchedulerSandboxActivitiesClient();

        using ServiceProvider provider = services.BuildServiceProvider();
        SandboxActivitiesClient client = provider.GetRequiredService<SandboxActivitiesClient>();

        // Act
        await client.RemoveSandboxActivityDeclarationAsync("profile-a");

        // Assert
        callInvoker.RemoveRequest.Should().NotBeNull();
        callInvoker.RemoveRequest!.WorkerProfileId.Should().Be("profile-a");
        callInvoker.RemoveHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == "client-test-taskhub");
    }

    [Fact]
    public async Task EnableSandboxActivitiesAsync_SendsWorkerProfileDeclarations()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        SandboxActivitiesClient client = new(
            new SandboxActivitiesGrpcTransport(new SandboxActivities.SandboxActivitiesClient(callInvoker)),
            "client-test-taskhub",
            new SandboxActivityDeclarationProvider());

        // Act
        await client.EnableSandboxActivitiesAsync();

        // Assert
        SandboxActivityDeclaration declaration = callInvoker.DeclareRequests
            .Should()
            .ContainSingle(request => request.WorkerProfileId == "client-test-profile")
            .Subject;
        declaration.ActivityNames.Should().Equal("ClientTestRemoteActivity");
        declaration.Image.ImageRef.Should().Be("example.com/client-test-worker:latest");
        callInvoker.DeclareHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == "client-test-taskhub");
        callInvoker.UnaryDisposeCount.Should().BeGreaterThan(0);
    }

    sealed class RecordingOnDemandSandboxLogCallInvoker : CallInvoker
    {
        public List<SandboxActivityDeclaration> DeclareRequests { get; } = [];

        public Metadata DeclareHeaders { get; private set; } = [];

        public RemoveSandboxActivityDeclarationRequest? RemoveRequest { get; private set; }

        public Metadata RemoveHeaders { get; private set; } = [];

        public int UnaryDisposeCount { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            if (method.FullName.EndsWith("/DeclareSandboxActivities", StringComparison.Ordinal))
            {
                this.DeclareRequests.Add(((SandboxActivityDeclaration)(object)request).Clone());
                this.DeclareHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new SandboxActivityDeclarationResult());
            }

            if (method.FullName.EndsWith("/RemoveSandboxActivityDeclaration", StringComparison.Ordinal))
            {
                this.RemoveRequest = (RemoveSandboxActivityDeclarationRequest)(object)request;
                this.RemoveHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new RemoveSandboxActivityDeclarationResult());
            }

            throw new NotSupportedException(method.FullName);
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotSupportedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotSupportedException();
        }

        AsyncUnaryCall<TResponse> CreateUnaryCall<TResponse>(TResponse response)
        {
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => new Status(StatusCode.OK, string.Empty),
                () => new Metadata(),
                () => this.UnaryDisposeCount++);
        }
    }

    static SandboxWorkerProfileOptions CreateDeclarationOptions()
    {
        SandboxWorkerProfileOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            ContainerImage = "mcr.microsoft.com/durabletask/demo-worker:1.0",
            ImagePullManagedIdentityClientId = "image-pull-client-id",
            SchedulerManagedIdentityClientId = "scheduler-client-id",
            Cpu = "500m",
            Memory = "1024Mi",
            MaxConcurrentActivities = 7,
        };
        options.AddActivity("RemoteHello");
        return options;
    }

    [SandboxWorkerProfile("client-test-profile")]
    sealed class ClientTestWorkerProfile : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            options.ContainerImage = "example.com/client-test-worker:latest";
            options.ImagePullManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.AddActivity("ClientTestRemoteActivity");
        }
    }

    [SandboxWorkerProfile("annotated-profile")]
    sealed class AnnotatedWorkerProfile : ISandboxWorkerProfile
    {
        public static int ConfigureCallCount { get; private set; }

        public void Configure(SandboxWorkerProfileOptions options)
        {
            ConfigureCallCount++;
            options.ContainerImage = "example.com/repo/annotated-worker:latest";
            options.ImagePullManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.EnvironmentVariables["CUSTOM_ENV"] = "configured-value";
            options.AddActivity("ConfiguredRemoteHello");
        }
    }

    [SandboxWorkerProfile("case-profile-a")]
    sealed class CaseDuplicateWorkerProfileA : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            if (Environment.GetEnvironmentVariable("DTS_TEST_ENABLE_CASE_DUPLICATE_SANDBOX_PROFILES") == "true")
            {
                options.AddActivity("CaseActivity");
            }
        }
    }

    [SandboxWorkerProfile("case-profile-b")]
    sealed class CaseDuplicateWorkerProfileB : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            if (Environment.GetEnvironmentVariable("DTS_TEST_ENABLE_CASE_DUPLICATE_SANDBOX_PROFILES") == "true")
            {
                options.AddActivity("caseactivity");
            }
        }
    }

    sealed class ProfileWithoutInterface
    {
    }

    sealed class EnvironmentVariableScope : IDisposable
    {
        readonly string name;
        readonly string? originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            this.name = name;
            this.originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(this.name, this.originalValue);
    }
}
