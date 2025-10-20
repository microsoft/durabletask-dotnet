# Microsoft.DurableTask.Generators

Source generators for `Microsoft.DurableTask`

For more information, see https://github.com/microsoft/durabletask-dotnet

## DurableTaskRegistryGenerator

Generates an extension method `AddAllTasks` for `DurableTaskRegistry` to register all orchestrations, activities, and entities defined in the project. This method will be internal, embedded, and in the global namespace.

### Usage
To use the method, simply call it on an instance of `DurableTaskRegistry`:

``` CSharp
services.AddDurableTaskWorker().AddTasks(registry =>
{
    registry.AddAllTasks();
});
```

### Exposing externally
If external exposure of this generated method is desired (publicly or via `InternalsVisisbleTo`), a manual wrapper method will be needed.

Exposing externally is useful for libraries containing durable tasks which are consumed by other projects.

``` CSharp
using Microsoft.DurableTask;

namespace My.Library.Namespace;

public static class DurableTaskRegistryExtensions
{
    public static DurableTaskRegistry AddMyLibraryTasks(this DurableTaskRegistry registry)
    {
        return registry.AddAllTasks();
    }
}
```
