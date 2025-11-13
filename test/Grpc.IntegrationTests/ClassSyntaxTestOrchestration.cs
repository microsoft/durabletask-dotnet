// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask;

namespace Microsoft.DurableTask.Grpc.Tests;

/// <summary>
/// Class-based orchestrator and activity implementations for integration tests.
/// </summary>
public static class ClassSyntaxTestOrchestration
{
    /// <summary>
    /// Orchestrator that calls multiple activities in sequence.
    /// </summary>
    [DurableTask("HelloSequence")]
    public class HelloSequenceOrchestrator : TaskOrchestrator<string?, List<string>>
    {
        public override async Task<List<string>> RunAsync(TaskOrchestrationContext context, string? input)
        {
            List<string> greetings =
            [
                await context.CallActivityAsync<string>("SayHello", "Tokyo"),
                await context.CallActivityAsync<string>("SayHello", "London"),
                await context.CallActivityAsync<string>("SayHello", "Seattle"),
            ];

            return greetings;
        }
    }

    /// <summary>
    /// Orchestrator that calls a activity which will throw exception.
    /// </summary>
    [DurableTask("FaultyOrchestrator")]
    public class FaultyOrchestrator : TaskOrchestrator<string?, string?>
    {
        public override async Task<string?> RunAsync(TaskOrchestrationContext context, string? input)
        {
            await context.CallActivityAsync<string>("ThrowingActivity");
            return null;
        }
    }

    /// <summary>
    /// Orchestrator that calls activities with retry option.
    [DurableTask("RetryOrchestrator")]
    public class RetryOrchestrator : TaskOrchestrator<string?, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string? input)
        {
            // Create retry policy: 3 attempts with 1ms interval
            var retryPolicy = new RetryPolicy(
                maxNumberOfAttempts: 3,
                firstRetryInterval: TimeSpan.FromMilliseconds(1));
            
            var options = TaskOptions.FromRetryPolicy(retryPolicy);

            string result = await context.CallActivityAsync<string>("RetryableActivity", options: options);
            return result;
        }
    }

    /// <summary>
    /// Orchestrator that calls a sub-orchestrator.
    /// </summary>
    [DurableTask("ParentOrchestrator")]
    public class ParentOrchestrator : TaskOrchestrator<int, int>
    {
        public override async Task<int> RunAsync(TaskOrchestrationContext context, int input)
        {
            int childResult = await context.CallSubOrchestratorAsync<int>("ChildOrchestrator", input: input);
            return input + childResult;
        }
    }

    /// <summary>
    /// Test sub-orchestrations.
    /// </summary>
    [DurableTask("ChildOrchestrator")]
    public class ChildOrchestrator : TaskOrchestrator<int, int>
    {
        public override async Task<int> RunAsync(TaskOrchestrationContext context, int input)
        {
            int result = await context.CallActivityAsync<int>("ChildActivity", input);
            return result;
        }
    }

    /// <summary>
    /// Orchestrator that waits for external event.
    /// </summary>
    [DurableTask("ExternalEventOrchestrator")]
    public class ExternalEventOrchestrator : TaskOrchestrator<string?, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string? input)
        {
            string eventData = await context.WaitForExternalEvent<string>("ApprovalEvent");
            return $"Received: {eventData}";
        }
    }

    [DurableTask("SayHello")]
    public class SayHelloActivity : TaskActivity<string, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, string city)
        {
            return Task.FromResult($"Hello {city}!");
        }
    }

    /// <summary>
    /// Activity that throws an exception.
    /// </summary>
    [DurableTask("ThrowingActivity")]
    public class ThrowingActivity : TaskActivity<string?, string?>
    {
        public override Task<string?> RunAsync(TaskActivityContext context, string? input)
        {
            throw new InvalidOperationException("Intentional failure.");
        }
    }

    /// <summary>
    /// Activity that throws exception on first attempt but succeed with third attemp.
    /// </summary>
    [DurableTask("RetryableActivity")]
    public class RetryableActivity : TaskActivity<string?, string>
    {
        static int attemptCount = 0;

        public override Task<string> RunAsync(TaskActivityContext context, string? input)
        {
            int currentAttempt = Interlocked.Increment(ref attemptCount);
            
            // Fail on first two attempts, succeed on third
            if (currentAttempt < 3)
            {
                throw new InvalidOperationException($"Attempt {currentAttempt} failed");
            }

            return Task.FromResult("Success after retries");
        }
    }

    [DurableTask("ChildActivity")]
    public class ChildActivity : TaskActivity<int, int>
    {
        public override Task<int> RunAsync(TaskActivityContext context, int input)
        {
            return Task.FromResult(input * 2);
        }
    }
}

