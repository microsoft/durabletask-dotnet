// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;

namespace DurableTask;

// TODO: Documentation
public sealed class TaskFailedException : Exception
{
    internal TaskFailedException(string taskName, int taskId, string errorName, string errorMessage, string? errorDetails)
        : base($"Activity task '{taskName}' (#{taskId}) failed with an unhandled exception: {errorMessage}")
    {
        this.TaskName = taskName;
        this.TaskId = taskId;
        this.ErrorName = errorName;
        this.ErrorMessage = errorMessage;
        this.ErrorDetails = errorDetails;
    }

    public string TaskName { get; }

    public int TaskId { get; }

    public string ErrorName { get; }

    public string ErrorMessage { get; }

    public string? ErrorDetails { get; }
}
