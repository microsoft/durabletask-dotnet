// ----------------------------------------------------------------------------------
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System.Threading.Tasks;
using Xunit;

namespace DurableTask.Generators.Tests;

public class OrchestratorTests
{
    [Fact]
    public Task PrimitiveTypes()
    {
        string code = @"
using System.Threading.Tasks;
using DurableTask;

[DurableTask(nameof(MyOrchestrator))]
class MyOrchestrator : TaskOrchestratorBase<int, string>
{
    protected override Task<string> OnRunAsync(int input) => Task.FromResult(string.Empty);
}";

        string expectedOutput = TestHelpers.WrapAndFormat(methodList: @"
/// <inheritdoc cref=""TaskHubClient.ScheduleNewOrchestrationInstanceAsync""/>
public static Task<string> ScheduleNewMyOrchestratorInstanceAsync(
    this TaskHubClient client,
    string? instanceId = null,
    int input = default,
    DateTimeOffset? startTime = null)
{
    return client.ScheduleNewOrchestrationInstanceAsync(
        ""MyOrchestrator"",
        instanceId,
        input,
        startTime);
}

/// <inheritdoc cref=""TaskOrchestrationContext.CallSubOrchestratorAsync""/>
public static Task<string> CallMyOrchestratorAsync(
    this TaskOrchestrationContext context,
    string? instanceId = null,
    int input = default,
    TaskOptions? options = null)
{
    return context.CallSubOrchestratorAsync<string>(
        ""MyOrchestrator"",
        instanceId,
        input,
        options);
}");

        return TestHelpers.RunTestAsync(code, expectedOutput);
    }
}
