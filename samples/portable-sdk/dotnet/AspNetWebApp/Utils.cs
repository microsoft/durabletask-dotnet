namespace AspNetWebApp;

static class Utils
{
    public static async Task ParallelForEachAsync<T>(this IEnumerable<T> items, int maxConcurrency, Func<T, Task> action)
    {
        List<Task> tasks;
        if (items is ICollection<T> itemCollection)
        {
            tasks = new List<Task>(itemCollection.Count);
        }
        else
        {
            tasks = [];
        }

        using SemaphoreSlim semaphore = new(maxConcurrency);
        foreach (T item in items)
        {
            tasks.Add(InvokeThrottledAction(item, action, semaphore));
        }

        await Task.WhenAll(tasks);
    }

    static async Task InvokeThrottledAction<T>(T item, Func<T, Task> action, SemaphoreSlim semaphore)
    {
        await semaphore.WaitAsync();
        try
        {
            await action(item);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
