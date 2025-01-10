# Hello World with the Durable Task SDK for .NET

In addition to [Durable Functions](https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-overview), the [Durable Task SDK for .NET](https://github.com/microsoft/durabletask-dotnet) can also use the Durable Task Scheduler service for managing orchestration state.

This directory includes a sample .NET console app that demonstrates how to use the Durable Task Scheduler with the Durable Task SDK for .NET (without any Azure Functions dependency).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PowerShell](https://docs.microsoft.com/powershell/scripting/install/installing-powershell)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)

## Creating a Durable Task Scheduler task hub

Before you can run the app, you need to create a Durable Task Scheduler task hub in Azure and produce a connection string that references it.

> **NOTE**: These are abbreviated instructions for simplicity. For a full set of instructions, see the Azure Durable Functions [QuickStart guide](../../../../quickstarts/HelloCities/README.md#create-a-durable-task-scheduler-namespace-and-task-hub).

1. Install the Durable Task Scheduler CLI extension:

    ```bash
    az upgrade
    az extension add --name durabletask --allow-preview true
    ```

1. Create a resource group:

    ```powershell
    az group create --name my-resource-group --location northcentralus
    ```

1. Create a Durable Task Scheduler namespace:

    ```powershell
    az durabletask namespace create -g my-resource-group --name my-namespace
    ```

1. Create a task hub within the namespace:

    ```powershell
    az durabletask taskhub create -g my-resource-group --namespace my-namespace --name "portable-dotnet"
    ```

1. Grant the current user permission to connect to the `portable-dotnet` task hub:

    ```powershell
    $subscriptionId = az account show --query "id" -o tsv
    $loggedInUser = az account show --query "user.name" -o tsv

    az role assignment create `
        --assignee $loggedInUser `
        --role "Durable Task Data Contributor" `
        --scope "/subscriptions/$subscriptionId/resourceGroups/my-resource-group/providers/Microsoft.DurableTask/namespaces/my-namespace/taskHubs/portable-dotnet"
    ```

    Note that it may take a minute for the role assignment to take effect.

1. Get the endpoint for the scheduler resource and save it to the `DURABLE_TASK_SCHEDULER_ENDPOINT_ADDRESS` environment variable:

    ```powershell
    $endpoint = az durabletask namespace show `
        -g my-resource-group `
        -n my-namespace `
        --query "properties.url" `
        -o tsv
    $env:DURABLE_TASK_SCHEDULER_ENDPOINT_ADDRESS = $endpoint
    ```

    The `DURABLE_TASK_SCHEDULER_ENDPOINT_ADDRESS` environment variable is used by the sample app to connect to the Durable Task Scheduler resource.

1. Save the task hub name to the `DURABLE_TASK_SCHEDULER_TASK_HUB_NAME` environment variable:

    ```powershell
    $env:DURABLE_TASK_SCHEDULER_TASK_HUB_NAME = "portable-dotnet"
    ```

    The `DURABLE_TASK_SCHEDULER_TASK_HUB_NAME` environment variable is to configure the sample app with the correct task hub resource name.

## Running the sample

In the same terminal window as above, use the following steps to run the sample on your local machine.

1. Clone this repository.

1. Open a terminal window and navigate to the `samples/portable-sdk/dotnet/AspNetWebApp` directory.

1. Run the following command to build and run the sample:

    ```bash
    dotnet run
    ```

You should see output similar to the following:

```plaintext
Building...
info: Microsoft.DurableTask[1]
      Durable Task gRPC worker starting.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5008
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime[0]
      Content root path: D:\projects\Azure-Functions-Durable-Task-Scheduler-Private-Preview\samples\portable-sdk\dotnet\AspNetWebApp
info: Microsoft.DurableTask[4]
      Sidecar work-item streaming connection established.
```

## View orchestrations in the dashboard

You can view the orchestrations in the Durable Task Scheduler dashboard by navigating to the namespace-specific dashboard URL in your browser.

Use the following PowerShell command to get the dashboard URL:

```powershell
$baseUrl = az durabletask namespace show `
    -g my-resource-group `
    -n my-namespace `
    --query "properties.dashboardUrl" `
    -o tsv
$dashboardUrl = "$baseUrl/taskHubs/portable-dotnet"
$dashboardUrl
```

The URL should look something like the following:

```plaintext
https://my-namespace-atdngmgxfsh0-db.northcentralus.durabletask.io/taskHubs/portable-dotnet
```

Once logged in, you should see the orchestrations that were created by the sample app. Below is an example of what the dashboard might look like (note that some of the details will be different than the screenshot):

![Durable Task Scheduler dashboard](/media/images/dtfx-sample-dashboard.png)


## Optional: Deploy to Azure Container Apps
1. Create an container app following the instructions in the [Azure Container App documentation](https://learn.microsoft.com/azure/container-apps/get-started?tabs=bash).
2. During step 1, specify the deployed container app code folder at samples\portable-sdk\dotnet\AspNetWebApp
3. Follow the instructions to create a user managed identity and assign the `Durable Task Data Contributor` role then attach it to the container app you created in step 1 at [Azure-Functions-Durable-Task-Scheduler-Private-Preview](..\..\..\..\docs\configure-existing-app.md#run-the-app-on-azure-net). Please skip section "Add required environment variables to app" since these environment variables are not required for deploying to container app.
4. Call the container app endpoint at `http://sampleapi-<your-container-app-name>.azurecontainerapps.io/api/orchestrators/HelloCities`, Sample curl command:

    ```bash
    curl -X POST "https://sampleapi-<your-container-app-name>.azurecontainerapps.io/api/orchestrators/HelloCities"
    ```
5. You should see the orchestration created in the Durable Task Scheduler dashboard.
