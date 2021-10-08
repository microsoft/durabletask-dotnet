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

using System.Threading.Tasks;
using Xunit;

namespace DurableTask.Generators.Tests;

public class ActivityTests
{
    [Fact]
    public Task PrimitiveTypes()
    {
        string code = @"
using DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<int, string>
{
    protected override string OnRun(int input) => default;
}";

        string expectedOutput = TestHelpers.WrapAndFormat(@"
public static Task<string> CallMyActivityAsync(this TaskOrchestrationContext ctx, int input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<string>(""MyActivity"", input, options);
}

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync(code, expectedOutput);
    }

    [Fact]
    public Task CustomTypes()
    {
        string code = @"
using MyNS;
using DurableTask;

[DurableTask(nameof(MyActivity))]
class MyActivity : TaskActivityBase<MyClass, MyClass>
{
    protected override MyClass OnRun(MyClass input) => default;
}

namespace MyNS
{
    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(@"
public static Task<MyNS.MyClass> CallMyActivityAsync(this TaskOrchestrationContext ctx, MyNS.MyClass input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<MyNS.MyClass>(""MyActivity"", input, options);
}

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
{
    builder.AddActivity<MyActivity>();
    return builder;
}");

        return TestHelpers.RunTestAsync(code, expectedOutput);
    }


    [Fact]
    public Task ExplicitNaming()
    {
        // The [DurableTask] attribute is expected to override the activity class name
        string code = @"
using MyNS;
using DurableTask;

namespace MyNS
{
    [DurableTask(""MyActivity"")]
    class MyActivityImpl : TaskActivityBase<MyClass, MyClass>
    {
        protected override MyClass OnRun(MyClass input) => default;
    }

    public class MyClass { }
}";

        string expectedOutput = TestHelpers.WrapAndFormat(@"
public static Task<MyNS.MyClass> CallMyActivityAsync(this TaskOrchestrationContext ctx, MyNS.MyClass input, TaskOptions? options = null)
{
    return ctx.CallActivityAsync<MyNS.MyClass>(""MyActivity"", input, options);
}

public static ITaskBuilder AddAllGeneratedTasks(this ITaskBuilder builder)
{
    builder.AddActivity<MyNS.MyActivityImpl>();
    return builder;
}");

        return TestHelpers.RunTestAsync(code, expectedOutput);
    }
}