Source generators for `Microsoft.DurableTask`

## Overview

The `Microsoft.DurableTask.Generators` package provides source generators that automatically generate type-safe extension methods for orchestrators and activities. The generator automatically detects whether you're using Azure Functions or the Durable Task Scheduler and generates appropriate code for your environment.

## Configuration

### Project Type Detection

By default, the generator automatically determines whether to generate Azure Functions-specific code or Durable Task Worker code based on project references. If your project references `Microsoft.Azure.Functions.Worker.Extensions.DurableTask`, it generates Functions-specific code. Otherwise, it generates code for the Durable Task Worker (including the Durable Task Scheduler).

### Explicit Project Type Configuration

In some scenarios, you may want to explicitly control the generator's behavior, such as when you have transitive dependencies on Functions packages but are building a Durable Task Worker application. You can configure this using the `DurableTaskGeneratorProjectType` MSBuild property in your `.csproj` file:

```xml
<PropertyGroup>
  <DurableTaskGeneratorProjectType>Standalone</DurableTaskGeneratorProjectType>
</PropertyGroup>
```

#### Supported Values

- `Auto` (default): Automatically detects project type based on referenced assemblies
- `Functions`: Forces generation of Azure Functions-specific code
- `Standalone`: Forces generation of standalone Durable Task Worker code (includes `AddAllGeneratedTasks` method)

#### Example: Force Standalone Mode

If your project has a transitive dependency on Azure Functions packages but you want to use the Durable Task Worker/Scheduler:

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

With this configuration, the generator will produce the `AddAllGeneratedTasks` extension method for worker registration:

```csharp
builder.Services.AddDurableTaskWorker(builder =>
{
    builder.AddTasks(r => r.AddAllGeneratedTasks());
});
```

For more information, see https://github.com/microsoft/durabletask-dotnet