using System;
using System.IO;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    [TestFixture]
    public class CaseInsensitiveStringEnumConverterTests
    {
        // Locks lowercase enum strings that already worked before the converter was added.
        [Test]
        public void Deserialize_PlayModeAction_AcceptsLowercaseAction()
        {
            JObject token = JObject.Parse("{\"action\":\"play\"}");

            ControlPlayModeSchema schema = token.ToObject<ControlPlayModeSchema>(
                UnityCliLoopToolParameterSerializer.CamelCaseSerializer);

            Assert.That(schema.Action, Is.EqualTo(PlayModeAction.Play));
        }

        // Locks the legacy PascalCase spelling for enums whose members are now lowercase, so scripts
        // written against the old documented form keep working (ADR 0006).
        [Test]
        public void Deserialize_SearchMode_AcceptsLegacyPascalCaseValue()
        {
            JObject token = JObject.Parse("{\"searchMode\":\"Contains\"}");

            FindGameObjectsSchema schema = token.ToObject<FindGameObjectsSchema>(
                UnityCliLoopToolParameterSerializer.CamelCaseSerializer);

            Assert.That(schema.SearchMode, Is.EqualTo(SearchMode.contains));
        }

        // Verifies invalid enum values surface the allowed action names in the error.
        [Test]
        public void Deserialize_PlayModeAction_InvalidValue_ListsValidValues()
        {
            JObject token = JObject.Parse("{\"action\":\"jump\"}");

            JsonSerializationException exception = Assert.Throws<JsonSerializationException>(() =>
                token.ToObject<ControlPlayModeSchema>(UnityCliLoopToolParameterSerializer.CamelCaseSerializer));

            Assert.That(exception.Message, Does.Contain("Valid values:"));
            Assert.That(exception.Message, Does.Contain("Play"));
            Assert.That(exception.Message, Does.Contain("Stop"));
            Assert.That(exception.Message, Does.Contain("Pause"));
            Assert.That(exception.Message, Does.Contain("Step"));
        }

        // Verifies integer enum tokens are rejected with an explicit string-only message.
        [Test]
        public void Deserialize_PlayModeAction_IntegerToken_RejectedWithExplicitMessage()
        {
            JObject token = JObject.Parse("{\"action\":1}");

            JsonSerializationException exception = Assert.Throws<JsonSerializationException>(() =>
                token.ToObject<ControlPlayModeSchema>(UnityCliLoopToolParameterSerializer.CamelCaseSerializer));

            Assert.That(exception.Message, Does.Contain("Enum parameter values must be JSON strings"));
            Assert.That(exception.Message, Does.Contain("Integer"));
            Assert.That(exception.Message, Does.Contain("PlayModeAction"));
        }

        /// <summary>
        /// Verifies a JSON null for a nullable enum parameter deserializes to null.
        /// </summary>
        [Test]
        public void Deserialize_WhenNullTokenForNullableEnum_ReturnsNull()
        {
            ConverterEnumDto dto = JsonConvert.DeserializeObject<ConverterEnumDto>(
                "{\"OptionalMode\":null}",
                CreateConverterSettings());

            Assert.That(dto.OptionalMode, Is.Null);
        }

        /// <summary>
        /// Verifies a JSON null for a non-nullable enum parameter is rejected with a message naming the enum.
        /// </summary>
        [Test]
        public void Deserialize_WhenNullTokenForNonNullableEnum_ThrowsNamingTheEnum()
        {
            JsonSerializationException exception = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<ConverterEnumDto>("{\"Mode\":null}", CreateConverterSettings()));

            Assert.That(exception.Message, Does.StartWith("Cannot convert null value to ConverterMode."));
        }

        /// <summary>
        /// Verifies an enum value is written as its member name string.
        /// </summary>
        [Test]
        public void Serialize_WhenEnumValue_WritesMemberName()
        {
            ConverterEnumDto dto = new ConverterEnumDto { Mode = ConverterMode.Second, OptionalMode = ConverterMode.First };

            string json = JsonConvert.SerializeObject(dto, CreateConverterSettings());

            Assert.That(json, Is.EqualTo("{\"Mode\":\"Second\",\"OptionalMode\":\"First\"}"));
        }

        /// <summary>
        /// Verifies the converter writes a JSON null when asked to write a null value.
        /// </summary>
        [Test]
        public void WriteJson_WhenValueIsNull_WritesJsonNull()
        {
            CaseInsensitiveStringEnumConverter converter = new CaseInsensitiveStringEnumConverter();
            StringWriter text = new StringWriter();
            JsonTextWriter writer = new JsonTextWriter(text);

            converter.WriteJson(writer, null, JsonSerializer.CreateDefault());
            writer.Flush();

            Assert.That(text.ToString(), Is.EqualTo("null"));
        }

        private static JsonSerializerSettings CreateConverterSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.Converters.Add(new CaseInsensitiveStringEnumConverter());
            return settings;
        }

        public enum ConverterMode
        {
            First,
            Second
        }

        public sealed class ConverterEnumDto
        {
            public ConverterMode Mode { get; set; }
            public ConverterMode? OptionalMode { get; set; }
        }
    }
}
