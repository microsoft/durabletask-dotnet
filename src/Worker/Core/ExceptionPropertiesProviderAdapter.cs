// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Adapts a Microsoft.DurableTask.Worker IExceptionPropertiesProvider to DurableTask.Core IExceptionPropertiesProvider.
/// </summary>
public sealed class ExceptionPropertiesProviderAdapter : global::DurableTask.Core.IExceptionPropertiesProvider
{
    readonly IExceptionPropertiesProvider inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionPropertiesProviderAdapter"/> class.
    /// </summary>
    /// <param name="inner">The inner provider to adapt.</param>
    public ExceptionPropertiesProviderAdapter(IExceptionPropertiesProvider inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Gets exception properties from the inner provider.
    /// </summary>
    /// <param name="exception">The exception to get properties for.</param>
    /// <returns>The exception properties dictionary.</returns>
    public IDictionary<string, object>? GetExceptionProperties(Exception exception)
        => this.inner.GetExceptionProperties(exception);
}
