// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace DtsPortableSdkEntityTests;


[Route("tests")]
[ApiController]
public class TestsController(
    DurableTaskClient durableTaskClient,
    ILogger<TestsController> logger) : ControllerBase
{
    readonly DurableTaskClient durableTaskClient = durableTaskClient;
    readonly ILogger<TestsController> logger = logger;

    // HTTPie command:
    // http POST http://localhost:5008/tests?prefix=xyz
    [HttpPost()]
    public async Task<ActionResult> RunTests([FromQuery] string? prefix)
    {
        var context = new TestContext(this.durableTaskClient, this.logger, CancellationToken.None);
        string result = await TestRunner.RunAsync(context, prefix);
        return this.Ok(result);
    }

    // HTTPie command:
    // http GET http://localhost:5008/tests?prefix=xyz
    [HttpGet()]
    public async Task<ActionResult> ListTests([FromQuery] string? prefix)
    {
        var context = new TestContext(this.durableTaskClient, this.logger, CancellationToken.None);
        string result = await TestRunner.RunAsync(context, prefix, listOnly: true);
        return this.Ok(result);
    }
}
