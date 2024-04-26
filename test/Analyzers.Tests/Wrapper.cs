// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Analyzers.Tests;

public static class Wrapper
{
    public static string WrapDurableFunctionOrchestration(string code)
    {
        return $@"
{Usings()}
class Orchestrator
{{
{code}
}}
";
    }

    public static string WrapTaskOrchestrator(string code)
    {
        return $@"
{Usings()}
{code}
";
    }

    public static string WrapFuncOrchestrator(string code)
    {
        return $@"
{Usings()}

public class Program
{{
    public static void Main()
    {{
        new ServiceCollection().AddDurableTaskWorker(builder =>
        {{
            builder.AddTasks(tasks =>
            {{
                {code}
            }});
        }});
    }}
}}
";
    }

    static string Usings()
    {
        return $@"
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
";
    }
}
