---
applyTo: "src/Grpc/**,src/Worker/**,src/InProcessTestHost/**,test/Grpc.IntegrationTests/**"
---
# gRPC Worker and Integration Tests

## Wire Compatibility Rules

`src/Grpc/orchestrator_service.proto` defines the wire contract between the SDK and the sidecar process.

- Do not remove, rename, or renumber any existing proto field or RPC. Field numbers are permanent.
- Adding new optional fields is backward-compatible. Adding required fields or changing field types is not.
- After modifying the proto, run `src/Grpc/refresh-protos.ps1` to pull the latest proto version. C# stubs are generated at build time by `Grpc.Tools` — not committed to source.
- Proto-consumer code in `src/Client/Grpc/ProtoUtils.cs` and `src/Worker/Grpc/` must be updated to handle any new fields added.

## Worker Dispatch Loop

`src/Worker/Core/` contains the orchestration and activity dispatch loop. This code runs under concurrent load.

- The `IEnumerable<OrchestratorAction>` returned by the orchestrator engine may be lazily evaluated — enumerate it exactly once (see `// IMPORTANT` in `src/InProcessTestHost/Sidecar/Dispatcher/TaskOrchestrationDispatcher.cs`).
- The receive loop in `src/InProcessTestHost/Sidecar/Dispatcher/WorkItemDispatcher.cs` assumes a single logical thread — do not introduce concurrent access to its internal state without an explicit concurrency mechanism.
- Changing `DurableTaskWorkerOptions.DataConverter` or `DurableTaskWorkerOptions.DefaultVersion` is a breaking change for in-flight orchestrations. Add a `// WARNING` comment and update the XML doc if you touch those properties.

## Writing Integration Tests

Integration tests in `test/Grpc.IntegrationTests/` require a gRPC sidecar. Follow this pattern:

1. Inherit from `IntegrationTestBase` — it manages the `GrpcSidecarFixture` lifecycle.
2. Register orchestrators and activities using `StartWorkerAsync(b => b.AddTasks(...))`.
3. Schedule orchestrations via the injected `DurableTaskClient` from the fixture.
4. Await completion with a timeout: `await client.WaitForInstanceCompletionAsync(instanceId, timeout)`.
5. Do not use `Task.Delay` for synchronization — use `WaitForInstanceCompletionAsync` or external events.

Add integration tests (not just unit tests) when the behavior change touches the gRPC dispatch path, retry logic, or cancellation handling.
