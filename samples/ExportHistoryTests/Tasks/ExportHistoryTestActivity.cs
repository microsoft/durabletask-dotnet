using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace ExportHistoryTests.Tasks;

[DurableTask]
public sealed class ExportHistoryTestActivity(ILogger<ExportHistoryTestActivity> logger)
    : TaskActivity<ExportHistoryActivityRequest, string>
{
    readonly ILogger<ExportHistoryTestActivity> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<string> RunAsync(TaskActivityContext context, ExportHistoryActivityRequest input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.DelayMilliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(input.DelayMilliseconds), CancellationToken.None);
        }

        string payload = input.Payload ?? new string('A', input.PayloadBytes <= 0 ? 64 : input.PayloadBytes);
        this.logger.LogInformation("Activity step {Step} executed for scenario {Scenario}", input.StepIndex, input.ScenarioName);
        return $"{input.ScenarioName}-{input.StepIndex}-{payload.Length}";
    }
}

public sealed record ExportHistoryActivityRequest(
    string ScenarioName,
    int StepIndex,
    int PayloadBytes = 256,
    string? Payload = null,
    int DelayMilliseconds = 10);

