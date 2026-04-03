// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Http;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.DurableTask.Tests.Http;

public class RegistrationTests
{
    [Fact]
    public void UseHttpActivities_DoesNotThrow()
    {
        // Verify that calling UseHttpActivities on a builder does not throw,
        // confirming the registration code path is valid.
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert — no exception means registration succeeded
        FluentActions.Invoking(() =>
        {
            services.AddDurableTaskWorker(builder =>
            {
                builder.UseHttpActivities();
            });
        }).Should().NotThrow();
    }

    [Fact]
    public void UseHttpActivities_RegistersHttpClient()
    {
        // Verify that UseHttpActivities registers the named HttpClient
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableTaskWorker(builder =>
        {
            builder.UseHttpActivities();
        });

        ServiceProvider sp = services.BuildServiceProvider();
        var httpClientFactory = sp.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull("UseHttpActivities should register IHttpClientFactory");
    }
}
