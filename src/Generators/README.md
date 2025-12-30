Source generators for `Microsoft.DurableTask`

## Overview

The `Microsoft.DurableTask.Generators` package provides source generators that automatically generate type-safe extension methods for orchestrators and activities. The generator automatically detects whether you're using Azure Functions or the Durable Task Scheduler and generates appropriate code for your environment.

## Configuration

### Project Type Detection

The generator uses intelligent automatic detection to determine the project type:

1. **Primary Detection**: Checks for Azure Functions trigger attributes (`OrchestrationTrigger`, `ActivityTrigger`, `EntityTrigger`) in your code
   - If any methods use these trigger attributes, it generates Azure Functions-specific code
   - Otherwise, it generates standalone Durable Task Worker code

2. **Fallback Detection**: If no trigger attributes are found, checks if `Microsoft.Azure.Functions.Worker.Extensions.DurableTask` is referenced
   - This handles projects that reference the Functions package but haven't defined triggers yet

This automatic detection solves the common issue where transitive dependencies on Functions packages would incorrectly trigger Functions mode even when not using Azure Functions.

### Explicit Project Type Configuration (Optional)

In rare scenarios where you need to override the automatic detection, you can explicitly configure the project type using the `DurableTaskGeneratorProjectType` MSBuild property in your `.csproj` file:

```xml
<PropertyGroup>
  <DurableTaskGeneratorProjectType>Standalone</DurableTaskGeneratorProjectType>
</PropertyGroup>
```

#### Supported Values

- `Auto` (default): Automatically detects project type using the intelligent detection described above
- `Functions`: Forces generation of Azure Functions-specific code
- `Standalone`: Forces generation of standalone Durable Task Worker code (includes `AddAllGeneratedTasks` method)

#### Example: Force Standalone Mode

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <DurableTaskGeneratorProjectType>Standalone</DurableTaskGeneratorProjectType>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Microsoft.DurableTask.Generators" OutputItemType="Analyzer" />
    <!-- Your other package references -->
  </ItemGroup>
</Project>
```

With standalone mode, the generator produces the `AddAllGeneratedTasks` extension method for worker registration:

```csharp
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r => r.AddAllGeneratedTasks());
});
```

For more information, see https://github.com/microsoft/durabletask-dotnet