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
    public void OnDemandSandboxWorkerProfileContract_DoesNotExposeRemovedOptions()
    {
        typeof(SandboxWorkerProfileOptions).GetProperty("LaunchCommand").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("WorkerProfileRetryMaxAttempts").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("WorkerProfileRetryDelay").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("ContainerImage").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("ImagePullManagedIdentityClientId").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "HeartbeatInterval",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("WakeupPort").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("Entrypoint").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty("Cmd").Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "WorkerRegistrationRetryInitialDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "WorkerRegistrationRetryMaxDelay",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfileOptions).GetProperty(
            "Mode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Should().BeNull();
        typeof(SandboxWorkerProfile).GetProperty("LaunchCommand").Should().BeNull();
    }

    [Fact]
    public void OnDemandSandboxWorkerProfileContract_ExposesProfileActivityMethods()
    {
        // Arrange
        Type optionsType = typeof(SandboxWorkerProfileOptions);
        Type? activityAttributeType = typeof(SandboxWorkerProfileOptions).Assembly.GetType(
            "Microsoft.DurableTask.Client.AzureManaged.SandboxesActivityAttribute");

        // Act/Assert
        optionsType.GetProperty("ActivityNames").Should().BeNull();
        optionsType.GetMethod("AddActivity", [typeof(string)]).Should().BeNull();
        optionsType.GetMethod("AddActivity", [typeof(string), typeof(string)]).Should().NotBeNull();
        optionsType.GetMethod("AddActivities", [typeof(IList<SandboxWorkerProfileOptions.Activity>)]).Should().NotBeNull();
        optionsType.GetMethods().Should().Contain(method =>
            method.Name == "AddActivity" && method.IsGenericMethodDefinition);
        activityAttributeType.Should().BeNull();
    }

    [Fact]
    public void OnDemandSandboxWorkerProfileContract_ExposesImageOptionsObject()
    {
        // Arrange
        Type imageOptionsType = typeof(SandboxWorkerProfileOptions.ImageOptions);

        // Act/Assert
        typeof(SandboxWorkerProfileOptions).GetProperty("Image")!.PropertyType.Should().Be(imageOptionsType);
        imageOptionsType.GetProperty("ImageRef").Should().NotBeNull();
        imageOptionsType.GetProperty("ManagedIdentityClientId").Should().NotBeNull();
        imageOptionsType.GetProperty("Entrypoint").Should().NotBeNull();
        imageOptionsType.GetProperty("Cmd").Should().NotBeNull();
    }

    [Theory]
    [InlineData("250m", "512Mi")]
    [InlineData("500m", "1024Mi")]
    [InlineData("500M", "1024Mi")]
    [InlineData("0.5", "1Gi")]
    [InlineData("0.25", "512")]
    [InlineData("1", "1024mi")]
    [InlineData("16", "32768Mi")]
    public void SandboxWorkerProfileBuilder_BuildWorkerProfile_AcceptsAdcResourceQuantities(
        string cpu,
        string memory)
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        SandboxWorkerProfile workerProfile = SandboxWorkerProfileBuilder.BuildWorkerProfile(
            options,
            SandboxWorkerProfileBuilder.ResolveActivities(options.Activities));

        // Assert
        workerProfile.Resources.Cpu.Should().Be(cpu);
        workerProfile.Resources.Memory.Should().Be(memory);
    }

    [Theory]
    [InlineData("0", "1024Mi", "CPU")]
    [InlineData("0m", "1024Mi", "CPU")]
    [InlineData("125m", "1024Mi", "CPU")]
    [InlineData("300m", "1024Mi", "CPU")]
    [InlineData("500.5m", "1024Mi", "CPU")]
    [InlineData("500Mi", "1024Mi", "CPU")]
    [InlineData("17", "1024Mi", "CPU")]
    [InlineData("999999999999999999999999999999", "1024Mi", "CPU")]
    [InlineData("500m", "0", "memory")]
    [InlineData("500m", "0Mi", "memory")]
    [InlineData("500m", "0.1Gi", "memory")]
    [InlineData("500m", "512.5Mi", "memory")]
    [InlineData("500m", "1024.5", "memory")]
    [InlineData("250m", "513Mi", "memory")]
    [InlineData("500m", "2048Mi", "memory")]
    [InlineData("500m", "999999999999999999999999999999Gi", "memory")]
    [InlineData("500m", "500m", "memory")]
    public void SandboxWorkerProfileBuilder_BuildWorkerProfile_RejectsInvalidAdcResourceQuantities(
        string cpu,
        string memory,
        string expectedMessage)
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.Cpu = cpu;
        options.Memory = memory;

        // Act
        Action action = () => SandboxWorkerProfileBuilder.BuildWorkerProfile(
            options,
            SandboxWorkerProfileBuilder.ResolveActivities(options.Activities));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void SandboxWorkerProfileBuilder_ResolveActivities_DeduplicatesCaseInsensitively()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.AddActivity(" RemoteHello ", version: null);
        options.AddActivity("remotehello", version: null);
        options.AddActivity("Other", "v1");
        options.AddActivity("other", "V1");

        // Act
        SandboxActivityMetadata.Activity[] activities = SandboxWorkerProfileBuilder.ResolveActivities(options.Activities);

        // Assert
        activities.Should().Equal(
            new SandboxActivityMetadata.Activity("RemoteHello", Version: null),
            new SandboxActivityMetadata.Activity("Other", "v1"));
    }

    [Fact]
    public void SandboxWorkerProfileProvider_ResolveWorkerProfiles_DetectsVersionCaseInsensitiveActivityOwnership()
    {
        // Arrange
        using EnvironmentVariableScope enableDuplicateVersionCaseProfiles = new(
            "DTS_TEST_ENABLE_VERSION_CASE_DUPLICATE_SANDBOX_PROFILES",
            "true");
        SandboxWorkerProfileProvider provider = new();

        // Act
        Action action = () => provider.ResolveWorkerProfiles(TaskHub);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("VersionCaseActivity", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("version-case-profile-a", StringComparison.Ordinal)
                && ex.Message.Contains("version-case-profile-b", StringComparison.Ordinal));
    }

    [Fact]
    public void SandboxWorkerProfileBuilder_ResolveActivities_UsesAddedActivityRange()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.AddActivities(
            [
                new SandboxWorkerProfileOptions.Activity(" RangeA ", Version: null),
                new SandboxWorkerProfileOptions.Activity("RangeB", " v1 "),
            ]);

        // Act
        SandboxActivityMetadata.Activity[] activities = SandboxWorkerProfileBuilder.ResolveActivities(options.Activities);

        // Assert
        activities.Should().ContainInOrder(
            new SandboxActivityMetadata.Activity("RemoteHello", Version: null),
            new SandboxActivityMetadata.Activity("RangeA", Version: null),
            new SandboxActivityMetadata.Activity("RangeB", "v1"));
    }

    [Fact]
    public void SandboxWorkerProfileBuilder_BuildWorkerProfile_RequiresSchedulerManagedIdentityClientId()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.SchedulerManagedIdentityClientId = " ";

        // Act
        Action action = () => SandboxWorkerProfileBuilder.BuildWorkerProfile(
            options,
            SandboxWorkerProfileBuilder.ResolveActivities(options.Activities));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID*");
    }

    [Fact]
    public void SandboxWorkerProfileBuilder_BuildWorkerProfile_RequiresImageManagedIdentityClientId()
    {
        // Arrange
        SandboxWorkerProfileOptions options = CreateWorkerProfileOptions();
        options.Image.ManagedIdentityClientId = " ";

        // Act
        Action action = () => SandboxWorkerProfileBuilder.BuildWorkerProfile(
            options,
            SandboxWorkerProfileBuilder.ResolveActivities(options.Activities));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*managed identity client ID used to pull the worker image*");
    }

    [Fact]
    public void SandboxWorkerProfileProvider_ResolveWorkerProfiles_UsesWorkerProfileConfigure()
    {
        // Arrange
        using EnvironmentVariableScope image = new("DTS_ON_DEMAND_SANDBOX_ACTIVITY_IMAGE", "example.com/not-used:latest");
        using EnvironmentVariableScope cpu = new("DTS_ON_DEMAND_SANDBOX_CPU", "2000m");
        using EnvironmentVariableScope memory = new("DTS_ON_DEMAND_SANDBOX_MEMORY", "4096Mi");
        using EnvironmentVariableScope maxActivities = new("DTS_SANDBOX_MAX_ACTIVITIES", "99");

        SandboxWorkerProfileProvider provider = new();

        // Act
        SandboxWorkerProfileOptions options = provider.ResolveWorkerProfiles(TaskHub)
            .Single(options => options.WorkerProfileId == "annotated-profile");
        SandboxWorkerProfile workerProfile = SandboxWorkerProfileBuilder.BuildWorkerProfile(
            options,
            SandboxWorkerProfileBuilder.ResolveActivities(options.Activities));

        // Assert
        workerProfile.WorkerProfileId.Should().Be("annotated-profile");
        workerProfile.Activities.Select(static activity => activity.Name).Should().Equal("ConfiguredRemoteHello");
        workerProfile.Activities.Select(static activity => activity.Version).Should().Equal("v1");
        workerProfile.Image.ImageRef.Should().Be("example.com/repo/annotated-worker:latest");
        workerProfile.Image.ManagedIdentityClientId.Should().Be("image-pull-client-id");
        workerProfile.SchedulerManagedIdentityClientId.Should().Be("scheduler-client-id");
        workerProfile.Resources.Cpu.Should().Be("500m");
        workerProfile.Resources.Memory.Should().Be("1024Mi");
        workerProfile.MaxConcurrentActivities.Should().Be(4);
        workerProfile.EnvironmentVariables.Should().ContainKey("CUSTOM_ENV").WhoseValue.Should().Be("configured-value");
        workerProfile.Image.Entrypoint.Should().BeEmpty();
        workerProfile.Image.Cmd.Should().BeEmpty();
    }

    [Fact]
    public void SandboxWorkerProfileProvider_ValidateProfileType_RequiresProfileInterface()
    {
        // Arrange
        // Act
        Action action = () => SandboxWorkerProfileProvider.ValidateProfileType(typeof(ProfileWithoutInterface));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ISandboxWorkerProfile)}*");
    }

    [Fact]
    public void SandboxWorkerProfileProvider_ResolveWorkerProfiles_DetectsCaseInsensitiveActivityOwnership()
    {
        // Arrange
        using EnvironmentVariableScope enableDuplicateCaseProfiles = new(
            "DTS_TEST_ENABLE_CASE_DUPLICATE_SANDBOX_PROFILES",
            "true");
        SandboxWorkerProfileProvider provider = new();

        // Act
        Action action = () => provider.ResolveWorkerProfiles(TaskHub);

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
        await client.RemoveSandboxWorkerProfileAsync("profile-a");

        // Assert
        callInvoker.RemoveRequest.Should().NotBeNull();
        callInvoker.RemoveRequest!.WorkerProfileId.Should().Be("profile-a");
        callInvoker.RemoveHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == "client-test-taskhub");
    }

    [Fact]
    public async Task EnableSandboxActivitiesAsync_SendsWorkerProfiles()
    {
        // Arrange
        RecordingOnDemandSandboxLogCallInvoker callInvoker = new();
        SandboxActivitiesClient client = new(
            new SandboxActivitiesGrpcTransport(new SandboxActivities.SandboxActivitiesClient(callInvoker)),
            "client-test-taskhub",
            new SandboxWorkerProfileProvider());

        // Act
        await client.EnableSandboxActivitiesAsync();

        // Assert
        SandboxWorkerProfile workerProfile = callInvoker.DeclareRequests
            .Should()
            .ContainSingle(request => request.WorkerProfileId == "client-test-profile")
            .Subject;
        workerProfile.Activities.Select(static activity => activity.Name).Should().Equal("ClientTestRemoteActivity");
        workerProfile.Activities.Select(static activity => activity.Version).Should().Equal("v1");
        workerProfile.Image.ImageRef.Should().Be("example.com/client-test-worker:latest");
        callInvoker.DeclareHeaders.Should().Contain(header => header.Key == "taskhub" && header.Value == "client-test-taskhub");
        callInvoker.UnaryDisposeCount.Should().BeGreaterThan(0);
    }

    sealed class RecordingOnDemandSandboxLogCallInvoker : CallInvoker
    {
        public List<SandboxWorkerProfile> DeclareRequests { get; } = [];

        public Metadata DeclareHeaders { get; private set; } = [];

        public RemoveSandboxWorkerProfileRequest? RemoveRequest { get; private set; }

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
            if (method.FullName.EndsWith("/DeclareSandboxWorkerProfile", StringComparison.Ordinal))
            {
                this.DeclareRequests.Add(((SandboxWorkerProfile)(object)request).Clone());
                this.DeclareHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new DeclareSandboxWorkerProfileResult());
            }

            if (method.FullName.EndsWith("/RemoveSandboxWorkerProfile", StringComparison.Ordinal))
            {
                this.RemoveRequest = (RemoveSandboxWorkerProfileRequest)(object)request;
                this.RemoveHeaders = options.Headers ?? [];
                return CreateUnaryCall((TResponse)(object)new RemoveSandboxWorkerProfileResult());
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

    static SandboxWorkerProfileOptions CreateWorkerProfileOptions()
    {
        SandboxWorkerProfileOptions options = new()
        {
            TaskHub = TaskHub,
            WorkerProfileId = "profile-a",
            SchedulerManagedIdentityClientId = "scheduler-client-id",
            Cpu = "500m",
            Memory = "1024Mi",
            MaxConcurrentActivities = 7,
        };
        options.Image.ImageRef = "mcr.microsoft.com/durabletask/demo-worker:1.0";
        options.Image.ManagedIdentityClientId = "image-pull-client-id";
        options.AddActivity("RemoteHello", version: null);
        return options;
    }

    [SandboxWorkerProfile("client-test-profile")]
    sealed class ClientTestWorkerProfile : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            options.Image.ImageRef = "example.com/client-test-worker:latest";
            options.Image.ManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.AddActivity("ClientTestRemoteActivity", "v1");
        }
    }

    [SandboxWorkerProfile("annotated-profile")]
    sealed class AnnotatedWorkerProfile : ISandboxWorkerProfile
    {
        public static int ConfigureCallCount { get; private set; }

        public void Configure(SandboxWorkerProfileOptions options)
        {
            ConfigureCallCount++;
            options.Image.ImageRef = "example.com/repo/annotated-worker:latest";
            options.Image.ManagedIdentityClientId = "image-pull-client-id";
            options.SchedulerManagedIdentityClientId = "scheduler-client-id";
            options.Cpu = "500m";
            options.Memory = "1024Mi";
            options.MaxConcurrentActivities = 4;
            options.EnvironmentVariables["CUSTOM_ENV"] = "configured-value";
            options.AddActivity("ConfiguredRemoteHello", "v1");
        }
    }

    [SandboxWorkerProfile("case-profile-a")]
    sealed class CaseDuplicateWorkerProfileA : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            if (Environment.GetEnvironmentVariable("DTS_TEST_ENABLE_CASE_DUPLICATE_SANDBOX_PROFILES") == "true")
            {
                options.AddActivity("CaseActivity", version: null);
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
                options.AddActivity("caseactivity", version: null);
            }
        }
    }

    [SandboxWorkerProfile("version-case-profile-a")]
    sealed class VersionCaseDuplicateWorkerProfileA : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            if (Environment.GetEnvironmentVariable("DTS_TEST_ENABLE_VERSION_CASE_DUPLICATE_SANDBOX_PROFILES") == "true")
            {
                options.AddActivity("VersionCaseActivity", version: "v1");
            }
        }
    }

    [SandboxWorkerProfile("version-case-profile-b")]
    sealed class VersionCaseDuplicateWorkerProfileB : ISandboxWorkerProfile
    {
        public void Configure(SandboxWorkerProfileOptions options)
        {
            if (Environment.GetEnvironmentVariable("DTS_TEST_ENABLE_VERSION_CASE_DUPLICATE_SANDBOX_PROFILES") == "true")
            {
                options.AddActivity("versioncaseactivity", version: "V1");
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
