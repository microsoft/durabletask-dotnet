//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------
//  Adapted from the original contract tests for SDK xUnit, signing and dependency checks.

using System.Reflection;
using Microsoft.DurableTask.Client;
using Xunit;

namespace DurableTask.LargePayloadPurge.Tests;

public class LargePayloadPurgeContractTests
{
    [Fact]
    public void PackageExportsOnlyTheStandaloneInterface()
    {
        // Arrange
        Type contract = typeof(IOrchestrationServiceLargePayloadPurgeClient);

        // Act
        Type[] exported = contract.Assembly.GetExportedTypes();

        // Assert
        Assert.True(contract.IsInterface);
        Assert.Equal("DurableTask.LargePayloadPurge", contract.Namespace);
        Assert.Equal("DurableTask.LargePayloadPurge.Abstractions", contract.Assembly.GetName().Name);
        Assert.Equal([contract], exported);
        Assert.Empty(contract.GetInterfaces());
        Assert.Equal(3, contract.GetMethods().Length);
    }

    [Fact]
    public void SetAcceptsExplicitChoiceAndCallerDeadlineAndCancellation()
    {
        // Arrange / Act / Assert
        AssertSignature(
            nameof(IOrchestrationServiceLargePayloadPurgeClient.SetLargePayloadAutoPurgeAsync),
            typeof(Task),
            [typeof(bool), typeof(DateTime), typeof(CancellationToken)],
            ["enabled", "deadlineUtc", "cancellationToken"]);
    }

    [Fact]
    public void GetReturnsCanonicalSdkTombstones()
    {
        // Arrange / Act / Assert
        AssertSignature(
            nameof(IOrchestrationServiceLargePayloadPurgeClient.GetLargePayloadsToPurgeAsync),
            typeof(Task<IReadOnlyList<LargePayloadTombstone>>),
            [typeof(int), typeof(DateTime), typeof(CancellationToken)],
            ["limit", "deadlineUtc", "cancellationToken"]);
    }

    [Fact]
    public void ReportAcceptsCanonicalSdkResults()
    {
        // Arrange / Act / Assert
        AssertSignature(
            nameof(IOrchestrationServiceLargePayloadPurgeClient.ReportLargePayloadPurgeResultsAsync),
            typeof(Task),
            [typeof(IReadOnlyList<LargePayloadPurgeResult>), typeof(DateTime), typeof(CancellationToken)],
            ["results", "deadlineUtc", "cancellationToken"]);
    }

    [Fact]
    public void ModelsComeFromSdkClientNotTheInterfacePackage()
    {
        // Arrange
        Assembly client = typeof(DurableTaskClient).Assembly;

        // Act
        Assembly[] modelAssemblies =
        [
            typeof(LargePayloadTombstone).Assembly,
            typeof(LargePayloadPurgeResult).Assembly,
            typeof(LargePayloadPurgeDisposition).Assembly,
        ];

        // Assert
        Assert.All(modelAssemblies, assembly => Assert.Same(client, assembly));
        Assert.NotSame(client, typeof(IOrchestrationServiceLargePayloadPurgeClient).Assembly);
    }

    [Fact]
    public void ContractUsesSdkSigningAndIndependentAssemblyVersion()
    {
        // Arrange
        AssemblyName sdk = typeof(DurableTaskClient).Assembly.GetName();

        // Act
        AssemblyName contract = typeof(IOrchestrationServiceLargePayloadPurgeClient).Assembly.GetName();

        // Assert
        Assert.Equal(new Version(0, 1, 0, 0), contract.Version);
        Assert.Equal("6A4C0315C2D1D937", Convert.ToHexString(contract.GetPublicKeyToken()!));
        Assert.Equal(sdk.GetPublicKeyToken(), contract.GetPublicKeyToken());
    }

    [Fact]
    public void ContractDependsOnSdkModelsWithoutAddingACoreReverseDependency()
    {
        // Arrange
        Assembly contract = typeof(IOrchestrationServiceLargePayloadPurgeClient).Assembly;
        Assembly core = typeof(Core.TaskHubClient).Assembly;

        // Act
        string?[] contractReferences = contract.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        string?[] coreReferences = core.GetReferencedAssemblies().Select(name => name.Name).ToArray();

        // Assert
        Assert.Contains("Microsoft.DurableTask.Client", contractReferences);
        Assert.DoesNotContain("DurableTask.Core", contractReferences);
        Assert.DoesNotContain("Microsoft.DurableTask.Worker", contractReferences);
        Assert.DoesNotContain("Microsoft.DurableTask.Grpc", contractReferences);
        Assert.DoesNotContain("Microsoft.DurableTask.Extensions.AzureBlobPayloads", contractReferences);
        Assert.DoesNotContain(contract.GetName().Name, coreReferences);
        Assert.DoesNotContain("Microsoft.DurableTask.Client", coreReferences);
    }

    static void AssertSignature(string methodName, Type returnType, Type[] parameterTypes, string[] parameterNames)
    {
        MethodInfo method = typeof(IOrchestrationServiceLargePayloadPurgeClient).GetMethod(methodName)!;
        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
        ParameterInfo[] parameters = method.GetParameters();
        Assert.Equal(parameterTypes, parameters.Select(parameter => parameter.ParameterType));
        Assert.Equal(parameterNames, parameters.Select(parameter => parameter.Name));
        Assert.All(parameters, parameter => Assert.False(parameter.IsOptional));
    }
}
