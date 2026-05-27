// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.AzureManaged.Serverless;

namespace Microsoft.DurableTask.Samples.Serverless.MainApp;

[ServerlessWorkerProfile("default")]
internal sealed class DefaultServerlessWorkerProfile : IServerlessWorkerProfile
{
    public void Configure(ServerlessOptions options)
    {
        options.ContainerImage = "serverless-remote-worker:local";
        options.Cpu = "1000m";
        options.Memory = "2048Mi";
        options.MaxConcurrentActivities = 1;
        options.EnvironmentVariables["SERVERLESS_SAMPLE_MARKER"] = "serverless-dotnet-sample-marker";
    }
}

internal static class ServerlessTaskNames
{
    public const string LocalEcho = "LocalEcho";
    public const string RemoteHello = "RemoteHello";
    public const string RemoteDelay = "RemoteDelay";
    public const string RemoteEnv = "RemoteEnv";
    public const string RemoteFail = "RemoteFail";
    public const string RemoteFlaky = "RemoteFlaky";
    public const string RemoteIndex = "RemoteIndex";
    public const string RemoteCrash = "RemoteCrash";
    public const string HelloOrchestrator = nameof(HelloOrchestrator);
    public const string LongRunningOrchestrator = nameof(LongRunningOrchestrator);
    public const string MixedLocalRemoteOrchestrator = nameof(MixedLocalRemoteOrchestrator);
    public const string MultiActivityOrchestrator = nameof(MultiActivityOrchestrator);
    public const string UndeclaredActivityOrchestrator = nameof(UndeclaredActivityOrchestrator);
    public const string FanOutOrchestrator = nameof(FanOutOrchestrator);
    public const string EnvOrchestrator = nameof(EnvOrchestrator);
    public const string ExceptionOrchestrator = nameof(ExceptionOrchestrator);
    public const string RetryOrchestrator = nameof(RetryOrchestrator);
    public const string TimerOrchestrator = nameof(TimerOrchestrator);
    public const string CrashOrchestrator = nameof(CrashOrchestrator);
}

[DurableTask(ServerlessTaskNames.LocalEcho)]
internal sealed class LocalEchoActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"local:{input}");
}

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteHello)]
internal sealed class RemoteHelloDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteDelay)]
internal sealed class RemoteDelayDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteEnv)]
internal sealed class RemoteEnvDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteFail)]
internal sealed class RemoteFailDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteFlaky)]
internal sealed class RemoteFlakyDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteIndex)]
internal sealed class RemoteIndexDeclaration;

[ServerlessActivity("default", Name = ServerlessTaskNames.RemoteCrash)]
internal sealed class RemoteCrashDeclaration;
