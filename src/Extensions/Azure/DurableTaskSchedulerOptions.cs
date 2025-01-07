using Azure.Core;

namespace DurableTask.Extensions.Azure;

// NOTE: These options definitions will eventually be provided by the Durable Task SDK itself.

/// <summary>
/// Options for configuring the Durable Task Scheduler.
/// </summary>
public class DurableTaskSchedulerOptions
{
    internal DurableTaskSchedulerOptions(string endpointAddress, string taskHubName, TokenCredential credential)
    {
        this.EndpointAddress = endpointAddress ?? throw new ArgumentNullException(nameof(endpointAddress));
        this.TaskHubName = taskHubName ?? throw new ArgumentNullException(nameof(taskHubName));
        this.Credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    /// <summary>
    /// The endpoint address of the Durable Task Scheduler resource.
    /// Expected to be in the format "https://{scheduler-name}.{region}.durabletask.io".
    /// </summary>
    public string EndpointAddress { get; }

    /// <summary>
    /// The name of the task hub resource associated with the Durable Task Scheduler resource.
    /// </summary>
    public string TaskHubName { get; }

    /// <summary>
    /// The credential used to authenticate with the Durable Task Scheduler task hub resource.
    /// </summary>
    public TokenCredential? Credential { get; }

    /// <summary>
    /// The resource ID of the Durable Task Scheduler resource.
    /// The default value is https://durabletask.io.
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// The worker ID used to identify the worker instance.
    /// The default value is a string containing the machine name and the process ID.
    /// </summary>
    public string? WorkerId { get; set; }
}