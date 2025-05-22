// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.CodeAnalysis.Testing;
using Dapr.DurableTask.Analyzers.Orchestration;

using VerifyCS = Dapr.DurableTask.Analyzers.Tests.Verifiers.CSharpAnalyzerVerifier<Dapr.DurableTask.Analyzers.Orchestration.IOOrchestrationAnalyzer>;

namespace Dapr.DurableTask.Analyzers.Tests.Orchestration;

public class IOOrchestrationTests
{
    [Fact]
    public async Task EmptyCodeHasNoDiag()
    {
        string code = @"";

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code);
    }

    [Fact]
    public async Task IOTypesAreBannedWithinAzureFunctionOrchestrations()
    {
        string code = Wrapper.WrapDurableFunctionOrchestration(@"
[Function(""Run"")]
void Method([OrchestrationTrigger] TaskOrchestrationContext context)
{
    var http1 = {|#0:new HttpClient()|};
    
    var blob1 = {|#1:new BlobServiceClient(""test"")|};
    var blob2 = {|#2:new BlobContainerClient(""test"",""test"")|};
    var blob3 = {|#3:new BlobClient(""test"",""test"",""test"")|};

    var queue1 = {|#4:new QueueServiceClient(""test"")|};
    var queue2 = {|#5:new QueueClient(""test"",""test"")|};

    var table1 = {|#6:new TableServiceClient(""test"")|};
    var table2 = {|#7:new TableClient(""test"",""test"")|};

    var cosmos1 = {|#8:new CosmosClient(""test"")|};

    var sql1 = {|#9:new SqlConnection()|};
}
");
        string[] types = [
            "HttpClient",
            "BlobServiceClient", "BlobContainerClient", "BlobClient",
            "QueueServiceClient", "QueueClient",
            "TableServiceClient", "TableClient",
            "CosmosClient",
            "SqlConnection",
        ];

        DiagnosticResult[] expected = types.Select(
            (type, i) => BuildDiagnostic().WithLocation(i).WithArguments("Method", type, "Run")).ToArray();

        await VerifyCS.VerifyDurableTaskAnalyzerAsync(code, expected);
    }

    static DiagnosticResult BuildDiagnostic()
    {
        return VerifyCS.Diagnostic(IOOrchestrationAnalyzer.DiagnosticId);
    }
}
