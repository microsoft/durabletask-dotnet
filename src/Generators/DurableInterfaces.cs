// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis;

namespace Microsoft.DurableTask.Generators;

readonly record struct DurableInterfaces(
    INamedTypeSymbol OrchestratorInterface,
    INamedTypeSymbol ActivityInterface,
    INamedTypeSymbol EntityInterface)
{
    public static DurableInterfaces Create(Compilation compilation)
    {
        INamedTypeSymbol? orchestratorInterface = compilation.GetOrchestratorInterface();
        INamedTypeSymbol? activityInterface = compilation.GetActivityInterface();
        INamedTypeSymbol? entityInterface = compilation.GetEntityInterface();

        if (orchestratorInterface is null || activityInterface is null || entityInterface is null)
        {
            return default;
        }

        return new DurableInterfaces(
            orchestratorInterface,
            activityInterface,
            entityInterface);
    }
}
