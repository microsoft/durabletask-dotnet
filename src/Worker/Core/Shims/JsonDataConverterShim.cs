// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CoreJsonDataConverter = DurableTask.Core.Serializing.JsonDataConverter;

namespace Microsoft.DurableTask.Worker.Shims;

/// <summary>
/// A shim to go from <see cref="DataConverter" /> to <see cref="CoreJsonDataConverter" />.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JsonDataConverterShim"/> class.
/// </remarks>
/// <param name="innerConverter">The converter to wrap.</param>
sealed class JsonDataConverterShim(DataConverter innerConverter) : CoreJsonDataConverter
{
    readonly DataConverter innerConverter = Check.NotNull(innerConverter);

    /// <inheritdoc/>
    public override string Serialize(object value)
        => this.innerConverter.Serialize(value);

    /// <inheritdoc/>
    public override string Serialize(object value, bool formatted)
        => this.Serialize(value);

    /// <inheritdoc/>
    public override object Deserialize(string data, Type objectType)
        => this.innerConverter.Deserialize(data, objectType);
}
