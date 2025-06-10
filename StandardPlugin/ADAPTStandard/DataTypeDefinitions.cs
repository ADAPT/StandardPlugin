//Auto-generated from quicktype.io

namespace AgGateway.ADAPT.DataTypeDefinitions
{
    using System;
    using System.Collections.Generic;

    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public partial class DataTypeDefinitions
    {
        [JsonProperty("dataTypeDefinitions")]
        public List<DataTypeDefinition> Definitions { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public partial class DataTypeDefinition
    {
        [JsonProperty("dataDefinitionBaseTypeCode")]
        public DataDefinitionBaseTypeCode DataDefinitionBaseTypeCode { get; set; }

        [JsonProperty("dataDefinitionStatusCode")]
        public StatusCode DataDefinitionStatusCode { get; set; }

        [JsonProperty("definitionCode")]
        public string DefinitionCode { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("numericDataTypeDefinitionAttributes", NullValueHandling = NullValueHandling.Ignore)]
        public NumericDataTypeDefinitionAttributes NumericDataTypeDefinitionAttributes { get; set; }

        [JsonProperty("scopes")]
        public List<string> Scopes { get; set; }

        [JsonProperty("enumeratedDataTypeDefinitionAttributes", NullValueHandling = NullValueHandling.Ignore)]
        public EnumeratedDataTypeDefinitionAttributes EnumeratedDataTypeDefinitionAttributes { get; set; }

        [JsonProperty("geoPoliticalContexts", NullValueHandling = NullValueHandling.Ignore)]
        public List<GeoPoliticalContext> GeoPoliticalContexts { get; set; }
    }

    public partial class EnumeratedDataTypeDefinitionAttributes
    {
        [JsonProperty("items")]
        public List<Item> Items { get; set; }
    }

    public partial class Item
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("enumerationItemStatusCode")]
        public StatusCode EnumerationItemStatusCode { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    public partial class GeoPoliticalContext
    {
        [JsonProperty("iSO3166-2Code")]
        public string ISo31662Code { get; set; }
    }

    public partial class NumericDataTypeDefinitionAttributes
    {
        [JsonProperty("numericDataTypeCode")]
        public NumericDataTypeCode NumericDataTypeCode { get; set; }

        [JsonProperty("unitOfMeasureCode", NullValueHandling = NullValueHandling.Ignore)]
        public string UnitOfMeasureCode { get; set; }
    }

    public enum DataDefinitionBaseTypeCode { Enumeration, Numeric, Text };

    public enum StatusCode { Valid };

    public enum NumericDataTypeCode { Double, Integer };

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
            {
                DataDefinitionBaseTypeCodeConverter.Singleton,
                StatusCodeConverter.Singleton,
                NumericDataTypeCodeConverter.Singleton,
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    internal class DataDefinitionBaseTypeCodeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(DataDefinitionBaseTypeCode) || t == typeof(DataDefinitionBaseTypeCode?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            switch (value)
            {
                case "ENUMERATION":
                    return DataDefinitionBaseTypeCode.Enumeration;
                case "NUMERIC":
                    return DataDefinitionBaseTypeCode.Numeric;
                case "TEXT":
                    return DataDefinitionBaseTypeCode.Text;
            }
            throw new Exception("Cannot unmarshal type DataDefinitionBaseTypeCode");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (DataDefinitionBaseTypeCode)untypedValue;
            switch (value)
            {
                case DataDefinitionBaseTypeCode.Enumeration:
                    serializer.Serialize(writer, "ENUMERATION");
                    return;
                case DataDefinitionBaseTypeCode.Numeric:
                    serializer.Serialize(writer, "NUMERIC");
                    return;
                case DataDefinitionBaseTypeCode.Text:
                    serializer.Serialize(writer, "TEXT");
                    return;
            }
            throw new Exception("Cannot marshal type DataDefinitionBaseTypeCode");
        }

        public static readonly DataDefinitionBaseTypeCodeConverter Singleton = new DataDefinitionBaseTypeCodeConverter();
    }

    internal class StatusCodeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(StatusCode) || t == typeof(StatusCode?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (value == "VALID")
            {
                return StatusCode.Valid;
            }
            throw new Exception("Cannot unmarshal type StatusCode");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (StatusCode)untypedValue;
            if (value == StatusCode.Valid)
            {
                serializer.Serialize(writer, "VALID");
                return;
            }
            throw new Exception("Cannot marshal type StatusCode");
        }

        public static readonly StatusCodeConverter Singleton = new StatusCodeConverter();
    }

    internal class NumericDataTypeCodeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(NumericDataTypeCode) || t == typeof(NumericDataTypeCode?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            switch (value)
            {
                case "Double":
                    return NumericDataTypeCode.Double;
                case "Integer":
                    return NumericDataTypeCode.Integer;
            }
            throw new Exception("Cannot unmarshal type NumericDataTypeCode");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (NumericDataTypeCode)untypedValue;
            switch (value)
            {
                case NumericDataTypeCode.Double:
                    serializer.Serialize(writer, "Double");
                    return;
                case NumericDataTypeCode.Integer:
                    serializer.Serialize(writer, "Integer");
                    return;
            }
            throw new Exception("Cannot marshal type NumericDataTypeCode");
        }

        public static readonly NumericDataTypeCodeConverter Singleton = new NumericDataTypeCodeConverter();
    }
}
