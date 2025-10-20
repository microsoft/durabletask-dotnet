// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DurableTask.Generators;

/// <summary>
/// A source generator that creates a registry extension method to register all Durable Tasks discovered in this
/// assembly. The extension method will be named AddAllTasks and will register each task with its specified name.
/// The implementation will be embedded and in the global namespace, ensuring it does not cause conflicts even with
/// InternalsVisibleTo. If an assembly wants to expose this method publicly (or internally), it can create a public
/// (or internal) wrapper method.
/// </summary>
[Generator(LanguageNames.CSharp)]
public partial class DurableTaskRegistryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<DurableTaskDetails>> durableTasks = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                TypeNames.DurableTaskAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                GetTaskDetails)
            .Where(static details => details is not null)
            .Select((details, _) => details!)
            .Collect();

        context.RegisterPostInitializationOutput(static context => context.AddEmbeddedAttributeDefinition());
        context.RegisterSourceOutput(durableTasks, Execute);
    }
}
