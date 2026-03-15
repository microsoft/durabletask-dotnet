// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// Tasks are organized into separate namespaces. The source generator will place
// each task's extension methods into its own namespace instead of Microsoft.DurableTask.

using Microsoft.DurableTask;
using NamespaceGenerationSample.Registrations;

// Approval-related tasks live in their own namespace.
// The generated CallRegistrationActivityAsync() and ScheduleNewApprovalOrchestratorInstanceAsync()
// extension methods will be generated in this namespace.
namespace NamespaceGenerationSample.Approvals
{
    /// <summary>
    /// An orchestrator that runs an approval workflow.
    /// The generated extension method ScheduleNewApprovalOrchestratorInstanceAsync()
    /// will be in the NamespaceGenerationSample.Approvals namespace.
    /// </summary>
    [DurableTask(nameof(ApprovalOrchestrator))]
    public class ApprovalOrchestrator : TaskOrchestrator<string, string>
    {
        public override async Task<string> RunAsync(TaskOrchestrationContext context, string requestId)
        {
            // Use the generated typed extension method (in the Registrations namespace)
            // By importing the Registrations namespace, we get access to CallRegistrationActivityAsync().
            string registrationResult = await context.CallRegistrationActivityAsync(42);

            return $"Approved request '{requestId}' with registration: {registrationResult}";
        }
    }
}

// Registration-related tasks in a separate namespace.
// The generated CallRegistrationActivityAsync() extension method will be in this namespace.
namespace NamespaceGenerationSample.Registrations
{
    /// <summary>
    /// An activity that performs registration.
    /// The generated extension method CallRegistrationActivityAsync()
    /// will be in the NamespaceGenerationSample.Registrations namespace.
    /// </summary>
    [DurableTask(nameof(RegistrationActivity))]
    public class RegistrationActivity : TaskActivity<int, string>
    {
        public override Task<string> RunAsync(TaskActivityContext context, int registrationId)
        {
            return Task.FromResult($"Registration-{registrationId} completed");
        }
    }
}
