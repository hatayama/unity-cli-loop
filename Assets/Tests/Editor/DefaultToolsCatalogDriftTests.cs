using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using ToolParameterInfo = io.github.hatayama.UnityCliLoop.ToolContracts.ParameterInfo;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies embedded CLI tool schemas stay aligned with Unity's live registry.
    /// </summary>
    public sealed class DefaultToolsCatalogDriftTests
    {
        private const string DefaultToolsPath = "cli/common/tools/default-tools.json";
        private static readonly string[] CliOwnedCommandsWithoutLiveUnityTools =
        {
            // Why: focus-window is implemented by the Go CLI through OS process focus
            // (`cli/common/clicore/focus.go`) and Unity only accepts a compatibility
            // notification in JsonRpcRequestProcessor; it must stay in the fallback
            // catalog even though the Unity registry intentionally has no live tool.
            // Keep this list explicit so future CLI-owned fallback commands fail the
            // guard first and get human review before being excluded.
            "focus-window"
        };

        [Test]
        public void DefaultToolsJson_WhenComparedWithLiveRegistry_DoesNotDrift()
        {
            // Verifies embedded CLI fallback schemas match live Unity parameter schemas.
            // The comparison intentionally normalizes inputSchema/parameterSchema, integer/number,
            // and enum order. It ignores descriptions, defaults, CLI-only hidden flags, array item
            // details that Unity schemas cannot express, explicitly listed CLI-owned commands that
            // have no live Unity registry tool, and embedded-only enum metadata when the live schema
            // cannot express enums for string-backed parameters. If the live schema later exposes an
            // enum for that property, the same embedded enum automatically becomes required to match.
            Dictionary<string, JObject> embeddedSchemas = ReadEmbeddedSchemas();
            Dictionary<string, JObject> liveSchemas = ReadLiveSchemas(embeddedSchemas.Keys.ToArray());
            string[] missingLiveTools = embeddedSchemas.Keys
                .Where(name => !CliOwnedCommandsWithoutLiveUnityTools.Contains(name))
                .Where(name => !liveSchemas.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missingLiveTools, Is.Empty, "Embedded tools must exist in the live Unity registry.");

            List<string> driftMessages = new();
            foreach (string toolName in embeddedSchemas.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (CliOwnedCommandsWithoutLiveUnityTools.Contains(toolName))
                {
                    continue;
                }

                JObject liveSchema = liveSchemas[toolName];
                JObject embeddedSchema = RemoveEmbeddedOnlyEnums(embeddedSchemas[toolName], liveSchema);
                if (JToken.DeepEquals(embeddedSchema, liveSchema))
                {
                    continue;
                }

                driftMessages.Add(
                    toolName +
                    "\nembedded: " +
                    embeddedSchema.ToString(Newtonsoft.Json.Formatting.None) +
                    "\nlive:     " +
                    liveSchema.ToString(Newtonsoft.Json.Formatting.None));
            }

            Assert.That(driftMessages, Is.Empty, string.Join("\n\n", driftMessages));
        }

        private static Dictionary<string, JObject> ReadEmbeddedSchemas()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, DefaultToolsPath));
            JObject catalog = JObject.Parse(json);
            JArray tools = catalog["tools"] as JArray ?? new JArray();

            return tools
                .OfType<JObject>()
                .ToDictionary(
                    tool => tool["name"]?.ToString() ?? "",
                    tool => NormalizeEmbeddedSchema(tool["inputSchema"] as JObject),
                    StringComparer.Ordinal);
        }

        private static Dictionary<string, JObject> ReadLiveSchemas(string[] embeddedToolNames)
        {
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            HashSet<string> embeddedNameSet = new(embeddedToolNames, StringComparer.Ordinal);

            return registry.GetRegisteredTools()
                .Where(tool => embeddedNameSet.Contains(tool.Name))
                .ToDictionary(
                    tool => tool.Name,
                    tool => NormalizeLiveSchema(tool.ParameterSchema),
                    StringComparer.Ordinal);
        }

        private static JObject NormalizeEmbeddedSchema(JObject schema)
        {
            JObject properties = schema?["properties"] as JObject ?? new JObject();
            JObject normalizedProperties = new();
            foreach (JProperty property in properties.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                normalizedProperties[property.Name] = NormalizeEmbeddedProperty(property.Value as JObject);
            }

            return new JObject
            {
                ["type"] = schema?["type"]?.ToString() ?? "",
                ["required"] = NormalizeStringArray(schema?["required"] as JArray),
                ["properties"] = normalizedProperties
            };
        }

        private static JObject NormalizeLiveSchema(ToolParameterSchema schema)
        {
            JObject normalizedProperties = new();
            foreach (KeyValuePair<string, ToolParameterInfo> property in
                schema.Properties.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                normalizedProperties[property.Key] = NormalizeLiveProperty(property.Value);
            }

            return new JObject
            {
                ["type"] = "object",
                ["required"] = new JArray(schema.Required),
                ["properties"] = normalizedProperties
            };
        }

        private static JObject NormalizeEmbeddedProperty(JObject property)
        {
            JObject normalized = new()
            {
                ["type"] = NormalizeSchemaType(property?["type"]?.ToString() ?? "")
            };

            JArray enumValues = property?["enum"] as JArray;
            if (enumValues != null)
            {
                normalized["enum"] = NormalizeEnumValues(enumValues.Values<string>());
            }

            return normalized;
        }

        private static JObject NormalizeLiveProperty(ToolParameterInfo property)
        {
            JObject normalized = new()
            {
                ["type"] = NormalizeSchemaType(property.Type)
            };

            if (property.Enum != null && property.Enum.Length > 0)
            {
                normalized["enum"] = NormalizeEnumValues(property.Enum);
            }

            return normalized;
        }

        private static JArray NormalizeStringArray(JArray values)
        {
            if (values == null)
            {
                return new JArray();
            }

            return new JArray(values.Values<string>());
        }

        private static string NormalizeSchemaType(string type)
        {
            // Why: the Unity generator maps every numeric CLR type to "number", while
            // the embedded CLI catalog has historically used "integer" for integral
            // values. Both are numeric parameters for current CLI parsing purposes.
            if (type == "integer")
            {
                return "number";
            }

            return type;
        }

        private static JArray NormalizeEnumValues(IEnumerable<string> values)
        {
            return new JArray(values.OrderBy(value => value, StringComparer.Ordinal));
        }

        private static JObject RemoveEmbeddedOnlyEnums(JObject embeddedSchema, JObject liveSchema)
        {
            JObject comparableSchema = (JObject)embeddedSchema.DeepClone();
            JObject embeddedProperties = comparableSchema["properties"] as JObject ?? new JObject();
            JObject liveProperties = liveSchema["properties"] as JObject ?? new JObject();

            foreach (JProperty embeddedProperty in embeddedProperties.Properties())
            {
                JObject liveProperty = liveProperties[embeddedProperty.Name] as JObject;
                if (liveProperty != null && liveProperty["enum"] != null)
                {
                    continue;
                }

                JObject embeddedPropertyValue = embeddedProperty.Value as JObject;
                embeddedPropertyValue?.Remove("enum");
            }

            return comparableSchema;
        }
    }
}
