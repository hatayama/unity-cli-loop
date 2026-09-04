using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Warnings is the single warning aggregate on pause point responses, that Warning is
    /// only ever its joined form, and that Message points at it.
    /// </summary>
    [TestFixture]
    public sealed class PausePointWarningsChannelTests
    {
        private const string SuppressionReason = "Suppressed while hot reload owns this method.";

        /// <summary>
        /// What: a hot-reload-suppressed status snapshot reports one warning on both fields, with
        /// Warning equal to the single Warnings entry.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenSuppressedByHotReload_KeepsWarningAndWarningsInAgreement()
        {
            UloopPausePointSnapshot snapshot = CreateSnapshot(true, SuppressionReason);

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.Warnings, Is.Not.Null);
            Assert.That(response.Warnings.Count, Is.EqualTo(1));
            Assert.That(response.Warning, Is.EqualTo(response.Warnings[0]));
            Assert.That(response.Warning, Is.EqualTo(SuppressionReason));
        }

        /// <summary>
        /// What: a suppression flag with no reason text omits both fields instead of emitting an
        /// empty Warning next to a missing Warnings array.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenSuppressedWithoutReason_OmitsBothWarningFields()
        {
            UloopPausePointSnapshot snapshot = CreateSnapshot(true, string.Empty);

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.Warning, Is.Null);
            Assert.That(response.Warnings, Is.Null);
        }

        /// <summary>
        /// What: an unsuppressed snapshot leaves both warning fields unset.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenNotSuppressed_OmitsBothWarningFields()
        {
            UloopPausePointSnapshot snapshot = CreateSnapshot(false, SuppressionReason);

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.Warning, Is.Null);
            Assert.That(response.Warnings, Is.Null);
        }

        /// <summary>
        /// What: assigning warnings ends Message with the count and a pointer at Warnings.
        /// </summary>
        [Test]
        public void Assign_WhenWarningsArePresent_EndsMessageWithTheWarningCountPointer()
        {
            PausePointResponse response = new() { Message = "Pause point enabled." };
            List<string> warnings = new() { "First warning.", "Second warning." };

            PausePointEnableWarningList.Assign(response, warnings);

            Assert.That(response.Message, Is.EqualTo("Pause point enabled. 2 warning(s). See Warnings."));
            Assert.That(response.Warning, Is.EqualTo("First warning. Second warning."));
        }

        /// <summary>
        /// What: assigning an empty warning list leaves Message untouched and nulls both warning
        /// properties, so NullValueHandling.Ignore omits the pair from the serialized response.
        /// </summary>
        [Test]
        public void Assign_WhenNoWarnings_LeavesMessageUnchangedAndOmitsBothWarningFields()
        {
            PausePointResponse response = new() { Message = "Pause point enabled." };

            PausePointEnableWarningList.Assign(response, new List<string>());

            Assert.That(response.Message, Is.EqualTo("Pause point enabled."));
            Assert.That(response.Warning, Is.Null);
            Assert.That(response.Warnings, Is.Null);
        }

        /// <summary>
        /// What: a clear --all that resumed Play Mode carries the warning on both fields and points
        /// Message at it, so the resume is not something the caller has to go looking for.
        /// </summary>
        [Test]
        public void FromClearAll_WhenResumedFromPause_PointsMessageAtTheWarning()
        {
            PausePointResponse response = PausePointResponse.FromClearAll(CreateClearAllResult(true));

            Assert.That(response.Warnings.Count, Is.EqualTo(1));
            Assert.That(response.Warning, Is.EqualTo(response.Warnings[0]));
            Assert.That(
                response.Message,
                Is.EqualTo("Pause points cleared. 1 warning(s). See Warnings."));
        }

        /// <summary>
        /// What: a clear --all that resumed nothing omits both warning fields and leaves Message
        /// unchanged.
        /// </summary>
        [Test]
        public void FromClearAll_WhenNothingResumed_LeavesMessageUnchanged()
        {
            PausePointResponse response = PausePointResponse.FromClearAll(CreateClearAllResult(false));

            Assert.That(response.Warnings, Is.Null);
            Assert.That(response.Warning, Is.Null);
            Assert.That(response.Message, Is.EqualTo("Pause points cleared."));
        }

        /// <summary>
        /// What: a response that warned about nothing serializes without either warning key, so the
        /// documented "both omitted when nothing warned" contract holds on enable and clear too.
        /// </summary>
        [Test]
        public void Assign_WhenNoWarnings_OmitsBothWarningKeysFromTheSerializedResponse()
        {
            PausePointResponse response = new() { Message = "Pause point enabled." };

            PausePointEnableWarningList.Assign(response, new List<string>());

            JObject serialized = JObject.Parse(JsonConvert.SerializeObject(response));

            Assert.That(serialized.ContainsKey("Warning"), Is.False);
            Assert.That(serialized.ContainsKey("Warnings"), Is.False);
        }

        /// <summary>
        /// What: the enable/clear Message suffix comes from the shared pointer helper, so it stays
        /// the same string hot reload appends.
        /// </summary>
        [Test]
        public void Assign_UsesTheSharedWarningsMessagePointer()
        {
            PausePointResponse response = new() { Message = "Pause point enabled." };

            PausePointEnableWarningList.Assign(response, new List<string> { "First warning." });

            Assert.That(
                response.Message,
                Is.EqualTo(WarningsMessagePointer.Append("Pause point enabled.", 1)));
        }

        private static UloopPausePointClearAllResult CreateClearAllResult(bool resumedFromPause)
        {
            return new UloopPausePointClearAllResult(
                1,
                new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc),
                new UloopPausePointEditorStateSnapshot(
                    true,
                    false,
                    UloopPausePointEditorStateCapturedAt.Current),
                new[] { "jump" },
                resumedFromPause);
        }

        private static UloopPausePointSnapshot CreateSnapshot(
            bool suppressedByHotReload,
            string suppressedByHotReloadReason)
        {
            return new UloopPausePointSnapshot(
                "jump",
                UloopPausePointStatus.Hit,
                true,
                true,
                1,
                1,
                30,
                UloopPausePointCaptureMode.SingleShot,
                20,
                15,
                2,
                Array.Empty<UloopPausePointCapturedHistoryFrame>(),
                0,
                false,
                "2026-06-03T00:00:00.0000000Z",
                1000,
                29000,
                1,
                new UloopPausePointEditorStateSnapshot(
                    true,
                    true,
                    UloopPausePointEditorStateCapturedAt.PausePointHit),
                "2026-06-03T00:00:01.0000000Z",
                "2026-06-03T00:00:01.0000000Z",
                1,
                1,
                "Pause point hit.",
                string.Empty,
                Array.Empty<UloopCapturedVariable>(),
                Array.Empty<UloopPausePointCallerFrame>(),
                false,
                Array.Empty<string>(),
                0,
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                false,
                suppressedByHotReload,
                suppressedByHotReloadReason,
                false,
                0,
                null,
                string.Empty,
                0,
                string.Empty);
        }
    }
}
