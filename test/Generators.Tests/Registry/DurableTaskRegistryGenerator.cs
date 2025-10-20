// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators.Tests;

public class DurableTaskRegistryGeneratorTests : GeneratorTest<DurableTaskRegistryGenerator>
{
    [Fact]
    public Task Generate_NoClasses_NoOutput()
    {
        GeneratorDriver driver = this.BuildDriver();
        return Verify(driver, this.Settings());
    }

    [Fact]
    public Task Generate_NoAttribute_NoOutput()
    {
        GeneratorDriver driver = this.BuildDriver(
            SourceHelpers.OrchestrationGeneric("MyOrchestrator1", attribute: false));
        return Verify(driver, this.Settings());
    }


    [Fact]
    public Task Generate_Abstract_NoOutput()
    {
        string orchestrator = SourceHelpers.OrchestrationGeneric("MyOrchestrator1")
            .Replace("public class", "public abstract class");
        GeneratorDriver driver = this.BuildDriver(orchestrator);
        return Verify(driver, this.Settings());
    }

    [Fact]
    public Task Generate_Multiple()
    {
        GeneratorDriver driver = this.BuildDriver(
            SourceHelpers.OrchestrationGeneric("MyOrchestrator1"),
            SourceHelpers.OrchestrationInterface("MyOrchestrator2"),
            SourceHelpers.ActivityGeneric("MyActivity2"),
            SourceHelpers.ActivityInterface("MyActivity2"),
            SourceHelpers.EntityGeneric("MyEntity1"),
            SourceHelpers.EntityInterface("MyEntity2"));

        return Verify(driver, this.Settings());
    }
}
