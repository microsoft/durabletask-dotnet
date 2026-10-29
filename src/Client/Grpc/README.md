A client implementation for `Microsoft.DurableTask`. This package provides a gRPC `DurableTaskClient` implementation for interacting with a task hub over gRPC.

Commonly used types:
- `GrpcDurableTaskClient`
- `GrpcDurableTaskClientOptions`

## Optional unary call deadlines

Set `GrpcCallTimeout` to a positive value to bound each unary gRPC call, not the whole operation.
The default is `null` (no deadline); an earlier explicit deadline is preserved.

```csharp
services.AddDurableTaskClient(builder => builder.UseGrpc(options =>
{
    options.Address = "http://localhost:4001";
    options.GrpcCallTimeout = TimeSpan.FromSeconds(30);
}));
```

The same property is available on `DurableTaskSchedulerClientOptions` when using `UseDurableTaskScheduler`.
`WaitForInstanceStartAsync`, `WaitForInstanceCompletionAsync`, and streaming calls are excluded.

Expiration fails the call with `StatusCode.DeadlineExceeded`. No retries are added, and a timed-out
mutation may still have been applied by the server.

For more information, see https://github.com/microsoft/durabletask-dotnet