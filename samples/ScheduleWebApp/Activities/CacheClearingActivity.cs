using Microsoft.DurableTask;

[DurableTask] // Optional: enables code generation for type-safe calls
public class CacheClearingActivity : TaskActivity<object, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, object input)
    {
        return Task.FromResult("hello");
    }
}