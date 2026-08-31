using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests strict JSON boolean metadata parsing shared by JSON-RPC request readers.
    /// </summary>
    public sealed class StrictJsonBooleanMetadataReaderTests
    {
        [Test]
        public void ReadOptionalBoolean_WhenPropertyIsBoolean_ReturnsValue()
        {
            // Verifies real JSON booleans are accepted without coercion.
            JObject metadata = JObject.Parse("{\"enabled\":true}");

            bool? value = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                metadata,
                "enabled",
                System.StringComparison.Ordinal);

            Assert.That(value, Is.True);
        }

        [Test]
        public void ReadOptionalBoolean_WhenPropertyIsString_ReturnsNull()
        {
            // Verifies string values are rejected instead of being coerced to booleans.
            JObject metadata = JObject.Parse("{\"enabled\":\"true\"}");

            bool? value = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                metadata,
                "enabled",
                System.StringComparison.Ordinal);

            Assert.That(value, Is.Null);
        }

        [Test]
        public void ReadOptionalBoolean_WhenPropertyUsesDifferentCaseAndOrdinalIgnoreCase_ReturnsValue()
        {
            // Verifies compile request params can keep their case-insensitive JSON contract.
            JObject metadata = JObject.Parse("{\"Enabled\":false}");

            bool? value = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                metadata,
                "enabled",
                System.StringComparison.OrdinalIgnoreCase);

            Assert.That(value, Is.False);
        }

        [Test]
        public void ReadOptionalBoolean_WhenPropertyIsMissing_ReturnsNull()
        {
            // Verifies absent optional flags stay unknown for callers that choose their own default.
            JObject metadata = JObject.Parse("{}");

            bool? value = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                metadata,
                "enabled",
                System.StringComparison.Ordinal);

            Assert.That(value, Is.Null);
        }

        [Test]
        public void ReadOptionalBoolean_WhenMetadataIsNull_ReturnsNull()
        {
            // Verifies absent metadata stays unknown for callers that choose their own default.
            bool? value = StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                null,
                "enabled",
                System.StringComparison.Ordinal);

            Assert.That(value, Is.Null);
        }
    }
}
