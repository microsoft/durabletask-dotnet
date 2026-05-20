// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.RemoteWorker;

public sealed record BurstMegaResult(int Index, int Value, string Host, int Pid);

public sealed record ResizeImageRequest(string SourceUri, int Width, int Height);

public sealed record ResizeImageResult(string SourceUri, int Width, int Height, string ThumbnailBase64, int SourceFingerprintLength);

[DurableTask("RemoteHello")]
internal sealed class RemoteHelloActivity : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string input)
        => Task.FromResult($"hello from {Environment.MachineName} pid={Environment.ProcessId}: {input}");
}

[DurableTask("BurstWork")]
internal sealed class BurstWorkActivity : TaskActivity<int, int>
{
    public override async Task<int> RunAsync(TaskActivityContext context, int input)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        return input * 2;
    }
}

[DurableTask("BurstMegaWork")]
internal sealed class BurstMegaWorkActivity : TaskActivity<int, BurstMegaResult>
{
    public override async Task<BurstMegaResult> RunAsync(TaskActivityContext context, int input)
    {
        int durationSeconds = 30;
        if (int.TryParse(Environment.GetEnvironmentVariable("DEMO_BURSTMEGA_DURATION_SECONDS"), out int parsed) && parsed > 0)
        {
            durationSeconds = parsed;
        }

        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        return new BurstMegaResult(input, input * 2, Environment.MachineName, Environment.ProcessId);
    }
}

[DurableTask("ResizeImage")]
internal sealed class ResizeImageActivity : TaskActivity<ResizeImageRequest, ResizeImageResult>
{
    public override Task<ResizeImageResult> RunAsync(TaskActivityContext context, ResizeImageRequest input)
    {
        byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{input.SourceUri}|{input.Width}|{input.Height}"));
        string thumbnail = Convert.ToBase64String(fingerprint[..24]);
        ResizeImageResult result = new(input.SourceUri, input.Width, input.Height, thumbnail, fingerprint.Length);
        return Task.FromResult(result);
    }
}
