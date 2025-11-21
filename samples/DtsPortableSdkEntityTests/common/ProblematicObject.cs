// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Core.Serialization;

namespace DtsPortableSdkEntityTests
{
    internal static class CustomSerialization
    {
        public static ProblematicObject CreateUnserializableObject()
        {
            return new ProblematicObject(serializable: false, deserializable: false);
        }

        public static ProblematicObject CreateUndeserializableObject()
        {
            return new ProblematicObject(serializable: true, deserializable: false);
        }

        /// <summary>
        /// An object for which we can inject errors on serialization or deserialization, to test
        // how those are handled by the framework.
        /// </summary>
        public class ProblematicObject
        {
            public ProblematicObject(bool serializable = true, bool deserializable = true)
            {
                this.Serializable = serializable;
                this.Deserializable = deserializable;
            }

            public bool Serializable { get; set; }

            public bool Deserializable { get; set; }
        }

        public class ProblematicObjectJsonConverter : JsonConverter<ProblematicObject>
        {
            public override ProblematicObject Read(
                ref Utf8JsonReader reader,
                Type typeToConvert,
                JsonSerializerOptions options)
            {
                bool deserializable = reader.GetBoolean();
                if (!deserializable)
                {
                    throw new JsonException("problematic object: is not deserializable");
                }
                return new ProblematicObject(serializable: true, deserializable: true);
            }

            public override void Write(
                Utf8JsonWriter writer,
                ProblematicObject value,
                JsonSerializerOptions options)
            {
                if (!value.Serializable)
                {
                    throw new JsonException("problematic object: is not serializable");
                }
                writer.WriteBooleanValue(value.Deserializable);
            }
        }
    }
}
