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
        // Verifies control-play-mode accepts lowercase enum tokens from the CLI.
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
    }
}
