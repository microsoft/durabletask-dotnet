// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Samples.Serverless.Declarer;

[DurableTask(nameof(HelloOrchestrator))]
internal sealed class HelloOrchestrator : TaskOrchestrator<string, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, string input)
    {
        string localResult = await context.CallActivityAsync<string>(ServerlessTaskNames.LocalEcho, input);
        string remoteResult = await context.CallActivityAsync<string>(ServerlessTaskNames.RemoteHello, input);
        return $"{localResult} | {remoteResult}";
    }
}

[DurableTask(nameof(BurstOrchestrator))]
internal sealed class BurstOrchestrator : TaskOrchestrator<int, List<int>>
{
    public override async Task<List<int>> RunAsync(TaskOrchestrationContext context, int input)
    {
        int activityCount = Math.Clamp(input, 1, 50);
        Task<int>[] tasks = Enumerable.Range(1, activityCount)
            .Select(i => context.CallActivityAsync<int>(ServerlessTaskNames.BurstWork, i))
            .ToArray();

        return (await Task.WhenAll(tasks)).ToList();
    }
}

[DurableTask(nameof(ResizeImageOrchestrator))]
internal sealed class ResizeImageOrchestrator : TaskOrchestrator<ResizeImageRequest, ResizeImageResult>
{
    public override Task<ResizeImageResult> RunAsync(TaskOrchestrationContext context, ResizeImageRequest input)
    => context.CallActivityAsync<ResizeImageResult>(ServerlessTaskNames.ResizeImage, input);
}

[DurableTask(nameof(BurstMegaOrchestrator))]
internal sealed class BurstMegaOrchestrator : TaskOrchestrator<int, List<BurstMegaResult>>
{
    public override async Task<List<BurstMegaResult>> RunAsync(TaskOrchestrationContext context, int input)
    {
        int activityCount = Math.Clamp(input, 1, 100);
        Task<BurstMegaResult>[] tasks = Enumerable.Range(1, activityCount)
            .Select(i => context.CallActivityAsync<BurstMegaResult>(ServerlessTaskNames.BurstMegaWork, i))
            .ToArray();

        return (await Task.WhenAll(tasks)).ToList();
    }
}
