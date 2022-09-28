// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CoreJsonDataConverter = DurableTask.Core.Serializing.JsonDataConverter;

namespace Microsoft.DurableTask;

partial class TaskOrchestrationShim
{
    sealed class JsonDataConverterShim : CoreJsonDataConverter
    {
        readonly DataConverter innerConverter;

        public JsonDataConverterShim(DataConverter innerConverter)
        {
            this.innerConverter = innerConverter;
        }

        public override string Serialize(object value)
            => this.innerConverter.Serialize(value);

        public override string Serialize(object value, bool formatted)
            => this.Serialize(value);

        public override object Deserialize(string data, Type objectType)
            => this.innerConverter.Deserialize(data, objectType);
    }
}
