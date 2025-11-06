// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;

namespace DtsPortableSdkEntityTests;

/// <summary>
///  An entity that records all batch positions and batch sizes
/// </summary>
class BatchEntity : ITaskEntity
{
    int operationCounter;

    public ValueTask<object?> RunAsync(TaskEntityOperation operation)
    {
        List<Entry>? state = (List<Entry>?) operation.State.GetState(typeof(List<Entry>));
        int batchNo;
        if (state == null)
        {
            batchNo = 0;
            state = new List<Entry>();
        }
        else if (operationCounter == 0)
        {
            batchNo = state.Last().batch + 1;
        }
        else
        {
            batchNo = state.Last().batch;
        }

        state.Add(new Entry(batchNo, operationCounter++));
        operation.State.SetState(state);
        return default;
    }

    public record struct Entry(int batch, int operation);

    public static void Register(DurableTaskRegistry r)
    {
        r.AddEntity(nameof(BatchEntity), _ => new BatchEntity());
    }
}
