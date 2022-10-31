// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Common options for <see cref="DurableTaskClient" />.
/// </summary>
public sealed class DurableTaskClientOptions
{
    DataConverter dataConverter = JsonDataConverter.Default;

    /// <summary>
    /// Gets or sets the data converter. Default value is <see cref="JsonDataConverter.Default" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is used for serializing inputs and outputs of <see cref="ITaskOrchestrator" />.
    /// </para><para>
    /// When set to <c>null</c>, this will revert to <see cref="JsonDataConverter.Default" />.
    /// </para><para>
    /// Alternatively, you may add a DataConverter as a singleton service to the service container and this will be
    /// populated from that service (only f not manually set).
    /// </para><para>
    /// WARNING: When changing this value, ensure backwards compatibility is preserved for any in-flight
    /// orchestrations. If it is not, deserialization may fail.
    /// </para>
    /// </remarks>
    public DataConverter DataConverter
    {
        get => this.dataConverter;
        set
        {
            if (value is null)
            {
                this.dataConverter = JsonDataConverter.Default;
                this.DataConverterExplicitlySet = false;
            }
            else
            {
                this.dataConverter = value;
                this.DataConverterExplicitlySet = true;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="DataConverter" /> was explicitly set or not.
    /// </summary>
    /// <remarks>
    /// This value is used to determine if we should resolve <see cref="DataConverter" /> from the
    /// <see cref="IServiceProvider" /> or not. If it is explicitly set (even to the default), we
    /// will <b>not</b> resolve it. If not set, we will attempt to resolve it. This is so the
    /// behavior is consistently irrespective of option configuration ordering.
    /// </remarks>
    internal bool DataConverterExplicitlySet { get; private set; }
}
