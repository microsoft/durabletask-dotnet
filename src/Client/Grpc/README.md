A client implementation for `Microsoft.DurableTask`. This package provides a gRPC `DurableTaskClient` implementation for interacting with a task hub over gRPC.

Commonly used types:
- `GrpcDurableTaskClient`
- `GrpcDurableTaskClientOptions`

## Optional unary call deadlines

By default, the client adds no gRPC deadline. To bound individual unary calls, configure a positive
`GrpcCallTimeout` when registering the client:

```csharp
services.AddDurableTaskClient(builder => builder.UseGrpc(options =>
{
    options.Address = "http://localhost:4001";
    options.GrpcCallTimeout = TimeSpan.FromSeconds(30);
}));
```

For Azure Durable Task Scheduler, configure the same option on the scheduler options:

```csharp
services.AddDurableTaskClient(builder => builder.UseDurableTaskScheduler(
    connectionString,
    options => options.GrpcCallTimeout = TimeSpan.FromSeconds(30)));
```

The timeout is captured when the client is constructed and bounds each unary RPC, not a whole
client operation, including any configured gRPC retries within that RPC.
An earlier deadline set through `GrpcDurableTaskClientOptions.Interceptors`
is preserved. Extremely large timeouts are capped at the latest finite UTC deadline supported by
`DateTime`. Caller cancellation remains independent and is not counted as a transport failure or
treated as proof of a healthy connection. A `Cancelled` response with no cancelled caller token
still resets the failure count like other application responses. gRPC does not identify the source
of a cancellation when server and caller cancellation race.

`WaitForInstanceStartAsync`, `WaitForInstanceCompletionAsync`, and streaming calls such as
`GetOrchestrationHistoryAsync` are excluded; use their cancellation tokens to bound those operations.
The option does not change worker streams or configure HTTP/2 keepalive.

A stalled unary call expires with `StatusCode.DeadlineExceeded`, which can contribute to the existing
channel recreation threshold. Scheduler-managed clients retain their cached-channel recovery and
interceptors. Supplying an external `CallInvoker` still opts out of channel recreation.

**A deadline does not guarantee that the server stopped processing the request.** This applies to
mutations such as scheduling, signaling, and purging as well as reads. This option adds no automatic
retry; determine the outcome before retrying a mutation. Channel recreation benefits subsequent
calls, not the failed call, and does not resolve an underlying server outage.

For more information, see https://github.com/microsoft/durabletask-dotnet