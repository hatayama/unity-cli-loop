#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies record-input refuses to start when the key filter names no key.
    /// Runs in PlayMode because the rejection sits behind the PlayMode preflight.
    /// </summary>
    public sealed class RecordInputKeyFilterRejectionTests
    {
        [TearDown]
        public void TearDown()
        {
            // A regression here starts a real recording, which would leak into the next test.
            if (InputRecorder.IsRecording)
            {
                InputRecorder.StopRecording();
            }
        }

        /// <summary>
        /// Tests that a key filter naming no key fails the command instead of recording every key.
        /// </summary>
        [UnityTest]
        public IEnumerator RecordInput_WhenTheKeyFilterNamesNoKey_FailsWithoutRecording()
        {
            RecordInputSchema request = new()
            {
                Action = RecordInputAction.Start,
                Keys = "3",
                DelaySeconds = 0,
                ShowOverlay = false
            };

            Task<RecordInputResponse> execution = new RecordInputUseCase().RecordInputAsync(request, CancellationToken.None);
            while (!execution.IsCompleted)
            {
                yield return null;
            }

            RecordInputResponse response = execution.Result;
            Assert.IsFalse(response.Success, response.Message);
            StringAssert.Contains("Invalid key name(s) in the keys filter: 3", response.Message);
            Assert.IsFalse(InputRecorder.IsRecording);
        }
    }
}
#endif
