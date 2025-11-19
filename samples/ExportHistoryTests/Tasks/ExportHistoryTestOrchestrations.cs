using Microsoft.DurableTask;

namespace ExportHistoryTests.Tasks;

public enum ExportInstanceOutcome
{
    Completed,
    Failed,
    LongRunning,
}

public sealed record ExportGenerationRequest(
    string ScenarioName,
    ExportInstanceOutcome Outcome,
    int ActivityCount = 3,
    bool UseFanOut = false,
    bool UseSubOrchestrator = false,
    bool UseLargePayload = false,
    bool EmitCustomStatus = false,
    int ActivityDelayMilliseconds = 25);

[DurableTask]
public sealed class ExportHistoryTestOrchestrator : TaskOrchestrator<ExportGenerationRequest, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, ExportGenerationRequest input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.EmitCustomStatus)
        {
            context.SetCustomStatus(new
            {
                scenario = input.ScenarioName,
                timestamp = context.CurrentUtcDateTime,
                outcome = input.Outcome.ToString(),
            });
        }

        List<Task> fanOut = new(input.ActivityCount);
        for (int step = 0; step < input.ActivityCount; step++)
        {
            int payloadBytes = input.UseLargePayload ? 8192 : 512;
            ExportHistoryActivityRequest activityRequest = new(
                input.ScenarioName,
                step,
                payloadBytes,
                Payload: null,
                DelayMilliseconds: input.ActivityDelayMilliseconds);

            Task<string> task = context.CallActivityAsync<string>(nameof(ExportHistoryTestActivity), activityRequest);
            if (input.UseFanOut)
            {
                fanOut.Add(task);
            }
            else
            {
                await task;
            }
        }

        if (fanOut.Count > 0)
        {
            await Task.WhenAll(fanOut);
        }

        if (input.UseSubOrchestrator)
        {
            await context.CallSubOrchestratorAsync<string>(nameof(ExportHistoryTestChildOrchestrator), input);
        }

        return input.Outcome switch
        {
            ExportInstanceOutcome.Completed => $"completed-{input.ScenarioName}",
            ExportInstanceOutcome.Failed => throw new InvalidOperationException($"Scenario {input.ScenarioName} forced failure."),
            ExportInstanceOutcome.LongRunning => await RunIndefinitelyAsync(context, input),
            _ => $"completed-{input.ScenarioName}",
        };
    }

    static async Task<string> RunIndefinitelyAsync(TaskOrchestrationContext context, ExportGenerationRequest input)
    {
        DateTime target = context.CurrentUtcDateTime.AddHours(1);
        await context.CreateTimer(target, CancellationToken.None);
        return $"longrunning-{input.ScenarioName}";
    }
}

[DurableTask]
public sealed class ExportHistoryTestChildOrchestrator : TaskOrchestrator<ExportGenerationRequest, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, ExportGenerationRequest input)
    {
        ExportHistoryActivityRequest activityRequest = new(
            $"{input.ScenarioName}-child",
            -1,
            128,
            Payload: null,
            DelayMilliseconds: 5);

        await context.CallActivityAsync<string>(nameof(ExportHistoryTestActivity), activityRequest);
        return $"child-{input.ScenarioName}";
    }
}

