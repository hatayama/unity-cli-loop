using System;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Deserializes string tokens into enum values using case-insensitive name matching.
    /// </summary>
    internal sealed class CaseInsensitiveStringEnumConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            Type enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return enumType.IsEnum;
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                Type underlyingType = Nullable.GetUnderlyingType(objectType);
                if (underlyingType != null)
                {
                    return null;
                }

                throw new JsonSerializationException(
                    $"Cannot convert null value to {objectType.Name}.");
            }

            if (reader.TokenType != JsonToken.String)
            {
                throw new JsonSerializationException(
                    $"Unexpected token {reader.TokenType} when parsing enum.");
            }

            string rawValue = reader.Value?.ToString() ?? string.Empty;
            Type enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            if (Enum.TryParse(enumType, rawValue, ignoreCase: true, out object parsed))
            {
                return parsed;
            }

            string[] validValues = Enum.GetNames(enumType);
            string pathSuffix = string.IsNullOrEmpty(reader.Path) ? string.Empty : $" Path '{reader.Path}'.";
            throw new JsonSerializationException(
                $"Error converting value \"{rawValue}\" to type '{enumType.Name}'.{pathSuffix} " +
                $"Valid values: {string.Join(", ", validValues)}.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.ToString());
        }
    }
}
