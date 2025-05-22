// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Dapr.DurableTask.Analyzers.Tests;

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
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.DurableTask;
using Dapr.DurableTask.Client;
using Dapr.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
";
    }
}
