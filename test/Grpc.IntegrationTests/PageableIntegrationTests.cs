// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Worker;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class PageableIntegrationTests : IntegrationTestBase
{
    public PageableIntegrationTests(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    {
    }

    [Fact]
    public async Task PageableActivity_Enumerates()
    {
        TaskName orchestratorName = nameof(PageableActivity_Enumerates);

        await using AsyncDisposable server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks
                .AddOrchestrator<string, int>(
                    orchestratorName, (ctx, input) => PageableOrchestrationAsync(ctx, input))
                .AddActivity<PageRequest, Page<string>>(
                    nameof(PageableActivityAsync), (_, input) => PageableActivityAsync(input)));
        });

        DurableTaskClient client = this.CreateDurableTaskClient();
        string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(orchestratorName, input: string.Empty);
        OrchestrationMetadata metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            this.TimeoutToken,
            getInputsAndOutputs: true);

        metadata.ReadOutputAs<int>().Should().Be(9);
    }

    static Task<Page<string>?> PageableActivityAsync(PageRequest? input)
    {
        int pageSize = input?.PageSize ?? 3;
        Page<string> CreatePage(string? next)
            => new (Enumerable.Range(0, pageSize).Select(x => $"item_{x}").ToList(), next);
        Page<string>? page = input?.Continuation switch
        {
            null => CreatePage("1"),
            "1" => CreatePage("2"),
            "2" => CreatePage(null),
            _ => null,
        };

        return Task.FromResult(page);
    }

    static async Task<int> PageableOrchestrationAsync(TaskOrchestrationContext context, string? input)
    {
        AsyncPageable<string> pageable = Pageable.Create((continuation, _, _) =>
        {
            return context.CallActivityAsync<Page<string>?>(nameof(PageableActivityAsync), new PageRequest(continuation))!;
        });

        return await pageable.CountAsync();
    }

    record PageRequest(string? Continuation, int? PageSize = null);
}
