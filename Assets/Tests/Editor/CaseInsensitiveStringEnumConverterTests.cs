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
    }
}
