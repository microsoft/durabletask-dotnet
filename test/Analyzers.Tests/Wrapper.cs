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

    /// <summary>
    /// Wraps code for TaskOrchestrator tests without Azure Functions dependencies.
    /// Used for SDK-only testing scenarios.
    /// </summary>
    public static string WrapTaskOrchestratorSdkOnly(string code)
    {
        return $@"
{UsingsForSdkOnly()}
{code}
";
    }

    /// <summary>
    /// Wraps code for FuncOrchestrator tests without Azure Functions dependencies.
    /// Used for SDK-only testing scenarios.
    /// </summary>
    public static string WrapFuncOrchestratorSdkOnly(string code)
    {
        return $@"
{UsingsForSdkOnly()}

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
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
";
    }

    static string UsingsForSdkOnly()
    {
        return $@"
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
";
    }
}
