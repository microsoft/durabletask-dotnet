Worker abstractions for `Microsoft.DurableTask`. A `DurableTaskWorker` is used for receiving and processing work from a task hub. This package does not include a concrete worker implementation. Instead a separate worker package must be used, such as `Microsoft.DurableTask.Worker.Grpc`.

Commonly used types:
- `DurableTaskWorker`
- `DurableTaskWorkerOptions`
- `IDurableTaskWorkerBuilder`

For more information, see https://github.com/microsoft/durabletask-dotnet/readme.md