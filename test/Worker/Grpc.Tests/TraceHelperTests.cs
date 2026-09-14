// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Tracing;

namespace Microsoft.DurableTask.Worker.Grpc.Tests;

public class TraceHelperTests
{
    [Fact]
    public void HasListeners_TracksMatchingActivityListener()
    {
        bool initialHasListeners = TraceHelper.HasListeners();
        ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Microsoft.DurableTask",
        };

        try
        {
            ActivitySource.AddActivityListener(listener);

            TraceHelper.HasListeners().Should().BeTrue();
        }
        finally
        {
            listener.Dispose();
        }

        TraceHelper.HasListeners().Should().Be(initialHasListeners);
    }
}
