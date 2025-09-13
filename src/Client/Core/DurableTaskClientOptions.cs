// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Common options for <see cref="DurableTaskClient" />.
/// </summary>
public class DurableTaskClientOptions
{
    DataConverter dataConverter = JsonDataConverter.Default;
    bool enableEntitySupport;
    bool enableLargePayloadSupport;

    /// <summary>
    /// Gets or sets the version of orchestrations that will be created.
    /// </summary>
    /// <remarks>
    /// Currently, this is sourced from the AzureManaged client options.
    /// </remarks>
    public string DefaultVersion { get; set; } = string.Empty;

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
    /// Gets or sets a value indicating whether this client should support entities. If true, all instance ids starting with '@' are reserved for entities,
    /// and validation checks are performed where appropriate.
    /// </summary>
    public bool EnableEntitySupport
    {
        get => this.enableEntitySupport;
        set
        {
            this.enableEntitySupport = value;
            this.EntitySupportExplicitlySet = true;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether this client should support large payloads using async serialization/deserialization.
    /// When enabled, the client will use async methods for serialization and deserialization to support externalized payloads.
    /// </summary>
    public bool EnableLargePayloadSupport
    {
        get => this.enableLargePayloadSupport;
        set
        {
            this.enableLargePayloadSupport = value;
            this.LargePayloadSupportExplicitlySet = true;
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

    /// <summary>
    /// Gets a value indicating whether <see cref="EnableEntitySupport" /> was explicitly set or not.
    /// </summary>
    internal bool EntitySupportExplicitlySet { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="EnableLargePayloadSupport" /> was explicitly set or not.
    /// </summary>
    internal bool LargePayloadSupportExplicitlySet { get; private set; }

    /// <summary>
    /// Applies these option values to another.
    /// </summary>
    /// <param name="other">The other options object to apply to.</param>
    internal void ApplyTo(DurableTaskClientOptions other)
    {
        if (other is not null)
        {
            // Make sure to keep this up to date as values are added.
            if (!other.DataConverterExplicitlySet)
            {
                other.DataConverter = this.DataConverter;
            }

            if (!other.EntitySupportExplicitlySet)
            {
                other.EnableEntitySupport = this.EnableEntitySupport;
            }

            if (!other.LargePayloadSupportExplicitlySet)
            {
                other.EnableLargePayloadSupport = this.EnableLargePayloadSupport;
            }

            if (!string.IsNullOrWhiteSpace(this.DefaultVersion))
            {
                other.DefaultVersion = this.DefaultVersion;
            }
        }
    }
}
