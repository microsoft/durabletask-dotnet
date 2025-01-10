using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace AspNetWebApp.Scenarios;

[DurableTask]
class HelloCities : TaskOrchestrator<string, List<string>>
{
    public override async Task<List<string>> RunAsync(TaskOrchestrationContext context, string input)
    {
        List<string> results =
        [
            await context.CallSayHelloAsync("Seattle"),
            await context.CallSayHelloAsync("Amsterdam"),
            await context.CallSayHelloAsync("Hyderabad"),
            await context.CallSayHelloAsync("Shanghai"),
            await context.CallSayHelloAsync("Tokyo"),
        ];
        return results;
    }
}

[DurableTask]
class SayHello : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string cityName)
    {
        return Task.FromResult($"Hello, {cityName}!");
    }
}
