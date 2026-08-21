using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins simulate-keyboard JSON so null optional diagnostics are omitted and set values stay present.
    /// </summary>
    public sealed class SimulateKeyboardResponseContractTests
    {
        private const string OmittedOptionalFieldsJson =
            "{\"Message\":\"\",\"Action\":\"\",\"InterruptedByPausePoint\":false,\"Success\":true}";

        private const string PopulatedOptionalFieldsJson =
            "{\"Message\":\"ok\",\"Action\":\"Press\",\"KeyName\":\"Space\","
            + "\"InterruptedByPausePoint\":false,\"RejectedByActivePausePointId\":\"marker\","
            + "\"PausePointId\":\"hit\",\"PausePointHitCount\":1,"
            + "\"PausePointHits\":[{\"Id\":\"hit\",\"HitCount\":1}],"
            + "\"PressEdgeObserved\":true,\"PressHoldExtendedFrames\":2,"
            + "\"PressEdgeConsumedByUpdateType\":\"Dynamic\","
            + "\"PressEdgeAnyDynamicUpdateObserved\":true,"
            + "\"PressEdgeKeyAlreadyPressedBeforeQueue\":false,"
            + "\"KeyStateTrackedHeld\":true,\"KeyStateDeviceIsPressed\":false,"
            + "\"ReleasedKeys\":[\"Space\"],\"Success\":true}";

        /// <summary>
        /// What: null optional simulate-keyboard fields are absent from production JSON, not serialized as null.
        /// </summary>
        [Test]
        public void SimulateKeyboardResponse_WhenOptionalFieldsAreNull_OmitsThoseKeysFromJson()
        {
            SimulateKeyboardResponse response = new SimulateKeyboardResponse();

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            Assert.That(json, Is.EqualTo(OmittedOptionalFieldsJson));
        }

        /// <summary>
        /// What: non-null optional simulate-keyboard fields serialize under their exact production keys.
        /// </summary>
        [Test]
        public void SimulateKeyboardResponse_WhenOptionalFieldsAreSet_IncludesThoseKeysInJson()
        {
            SimulateKeyboardResponse response = new SimulateKeyboardResponse
            {
                Success = true,
                Message = "ok",
                Action = "Press",
                KeyName = "Space",
                InterruptedByPausePoint = false,
                RejectedByActivePausePointId = "marker",
                PausePointId = "hit",
                PausePointHitCount = 1,
                PausePointHits = new List<UnityCliLoopPausePointHit>
                {
                    new UnityCliLoopPausePointHit { Id = "hit", HitCount = 1 }
                },
                PressEdgeObserved = true,
                PressHoldExtendedFrames = 2,
                PressEdgeConsumedByUpdateType = "Dynamic",
                PressEdgeAnyDynamicUpdateObserved = true,
                PressEdgeKeyAlreadyPressedBeforeQueue = false,
                KeyStateTrackedHeld = true,
                KeyStateDeviceIsPressed = false,
                ReleasedKeys = new List<string> { "Space" }
            };

            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            Assert.That(json, Is.EqualTo(PopulatedOptionalFieldsJson));
        }
    }
}
