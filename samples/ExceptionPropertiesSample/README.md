# Exception Properties Sample

This sample demonstrates how to use `IExceptionPropertiesProvider` to enrich `TaskFailureDetails` with custom exception properties for better diagnostics and error handling.

## Overview

When orchestrations or activities throw exceptions, the Durable Task framework captures failure details. By implementing `IExceptionPropertiesProvider`, you can extract custom properties from exceptions and include them in the `TaskFailureDetails`, making it easier to diagnose issues and handle errors programmatically.

## Key Concepts

1. **Custom Exception with Properties**: Create exceptions that carry additional context (error codes, metadata, etc.)
2. **IExceptionPropertiesProvider**: Implement this interface to extract custom properties from exceptions
3. **Automatic Property Extraction**: The framework automatically uses your provider when converting exceptions to `TaskFailureDetails`
4. **Retrieving Failure Details**: Use the durable client to retrieve orchestration status and access the enriched failure details

## What This Sample Does

1. Defines a `BusinessValidationException` with custom properties (ErrorCode, StatusCode, Metadata)
2. Implements `CustomExceptionPropertiesProvider` that extracts these properties from exceptions
3. Creates a validation orchestration and activity that throws the custom exception
4. Demonstrates how to retrieve and display failure details with custom properties using the durable client

## Running the Sample

This sample can run against either:

1. **Durable Task Scheduler (DTS)** (recommended): set the `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` environment variable.
2. **Local gRPC endpoint**: if the env var is not set, the sample uses the default local gRPC configuration.

### DTS

Set `DURABLE_TASK_SCHEDULER_CONNECTION_STRING` and run the sample.

```cmd
set DURABLE_TASK_SCHEDULER_CONNECTION_STRING=Endpoint=https://...;TaskHub=...;Authentication=...;
dotnet run --project ExceptionPropertiesSample
```

```bash
dotnet run --project samples/ExceptionPropertiesSample/ExceptionPropertiesSample.csproj
```

## Expected Output

The sample runs three test cases:
1. **Valid input**: Orchestration completes successfully
2. **Empty input**: Orchestration fails with custom properties (ErrorCode, StatusCode, Metadata)
3. **Short input**: Orchestration fails with different custom properties

For failed orchestrations, you'll see the custom properties extracted by the `IExceptionPropertiesProvider` displayed in the console.

## Code Structure

- `CustomExceptions.cs`: Defines the `BusinessValidationException` with custom properties
- `CustomExceptionPropertiesProvider.cs`: Implements `IExceptionPropertiesProvider` to extract properties
- `Tasks.cs`: Contains the orchestration and activity that throw custom exceptions
- `Program.cs`: Sets up the worker, registers the provider, and demonstrates retrieving failure details

## Key Code Snippet

```csharp
// Register the custom exception properties provider
builder.Services.AddSingleton<IExceptionPropertiesProvider, CustomExceptionPropertiesProvider>();

// Retrieve failure details with custom properties
OrchestrationMetadata result = await client.WaitForInstanceCompletionAsync(
    instanceId,
    getInputsAndOutputs: true); // Important: must be true to get failure details

if (result.FailureDetails?.Properties != null)
{
    foreach (var property in result.FailureDetails.Properties)
    {
        Console.WriteLine($"{property.Key}: {property.Value}");
    }
}
```

## Notes

- The `getInputsAndOutputs` parameter must be `true` when calling `GetInstanceAsync` or `WaitForInstanceCompletionAsync` to retrieve failure details
- Custom properties are only included if the orchestration is in a `Failed` state
- The `IExceptionPropertiesProvider` is called automatically by the framework when exceptions are caught

