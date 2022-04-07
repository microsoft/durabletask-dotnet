// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Logging;

namespace DurableTask;

record WorkerContext(
    IDataConverter DataConverter,
    ILogger Logger,
    IServiceProvider Services);