Client abstractions for `Microsoft.DurableTask`. A `DurableTaskClient` is used for interacting with a task hub. Including starting new orchestrations, retrieving orchestration details, sending events to orchestrations, etc. This package does not include a concrete client implementation. Instead a separate client package must be used, such as `Microsoft.DurableTask.Client.Grpc`.

Commonly used types:
- `DurableTaskClient`
- `DurableTaskClientOptions`
- `IDurableTaskClientProvider`
- `IDurableTaskClientBuilder`
- `IDurableTaskClientFactory`

For more information, see https://github.com/microsoft/durabletask-dotnet