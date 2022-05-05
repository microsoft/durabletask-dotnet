// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask;

record WorkerContext(
    DataConverter DataConverter,
    ILogger Logger,
    IServiceProvider Services);