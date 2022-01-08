// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask;

// TODO: This is public only because it's needed for Functions. Consider
//       a design that doesn't require this type to be public.
public interface IWorkerContext
{
    public IDataConverter DataConverter { get; }
}
