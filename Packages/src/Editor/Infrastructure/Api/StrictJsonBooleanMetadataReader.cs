using System;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads optional JSON metadata flags only when they are encoded as real JSON booleans.
    /// </summary>
    internal static class StrictJsonBooleanMetadataReader
    {
        internal static bool? ReadOptionalBoolean(
            JObject metadata,
            string propertyName,
            StringComparison propertyNameComparison)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrWhiteSpace(propertyName), "propertyName must not be empty");

            if (metadata == null)
            {
                return null;
            }

            JToken metadataToken = metadata.GetValue(propertyName, propertyNameComparison);
            if (metadataToken == null || metadataToken.Type != JTokenType.Boolean)
            {
                return null;
            }

            return metadataToken.Value<bool>();
        }
    }
}
