// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.Declarer;

internal static class ServerlessTaskNames
{
    public const string LocalEcho = "LocalEcho";
    public const string RemoteHello = "RemoteHello";
    public const string BurstWork = "BurstWork";
    public const string ResizeImage = "ResizeImage";
    public const string BurstMegaWork = "BurstMegaWork";
    public const string HelloOrchestrator = nameof(HelloOrchestrator);
    public const string BurstOrchestrator = nameof(BurstOrchestrator);
    public const string ResizeImageOrchestrator = nameof(ResizeImageOrchestrator);
    public const string BurstMegaOrchestrator = nameof(BurstMegaOrchestrator);
}

public sealed record BurstMegaResult(int Index, int Value, string Host, int Pid);

public sealed record ResizeImageRequest(string SourceUri, int Width, int Height);

public sealed record ResizeImageResult(string SourceUri, int Width, int Height, string ThumbnailBase64, int SourceFingerprintLength);

[DurableTask("LocalEcho")]
internal sealed class LocalEchoActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"local:{input}");
}
