using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the warning that asks to pause Play Mode before wiring added fields: when it is
    /// added, and how it changes the apply response.
    /// </summary>
    public sealed class HotReloadPauseBeforeWiringWarningTests
    {
        private const string SerializedFieldName = "Ns.Host.Label";

        [SetUp]
        public void SetUp()
        {
            // The response builder normalizes script paths, which needs the package roots the
            // production run captures at its entry point.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
        }

        /// <summary>
        /// What: the warning is added only when Play Mode runs unpaused and fields need wiring.
        /// </summary>
        [TestCase(true, false, true, true)]
        [TestCase(true, true, true, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, false, false, false)]
        public void Append_AddsTheWarningOnlyWhilePlayModeRunsUnpausedWithFieldsToWire(
            bool isPlaying,
            bool isPaused,
            bool namesFieldsToWire,
            bool expectWarning)
        {
            List<string> warnings = new List<string>();

            HotReloadPauseBeforeWiringWarning.Append(warnings, isPlaying, isPaused, namesFieldsToWire);

            Assert.That(
                warnings,
                expectWarning
                    ? Is.EqualTo(new[] { HotReloadConstants.PauseBeforeWiringDuringPlayWarning })
                    : Is.Empty);
        }

        /// <summary>
        /// What: a run in unpaused Play Mode that names a field to rewire gets the pause warning
        /// once and loses the single-compile sentence; the same result outside Play Mode keeps the
        /// sentence and gets no pause warning.
        /// </summary>
        [Test]
        public void Build_WhilePlayingWithRewireFields_AddsThePauseWarningAndOmitsSingleCompileResolution()
        {
            HotReloadResponse playing = Build(
                CreatePatchedResultWithTwoWarnings(serializedAddedFieldsReported: null),
                new[] { "Ns.Host.Speed" },
                isPlaying: true);
            HotReloadResponse notPlaying = Build(
                CreatePatchedResultWithTwoWarnings(serializedAddedFieldsReported: null),
                new[] { "Ns.Host.Speed" },
                isPlaying: false);
            HotReloadResponse playingWithoutFields = Build(
                CreatePatchedResultWithTwoWarnings(serializedAddedFieldsReported: null),
                Array.Empty<string>(),
                isPlaying: true);

            Assert.That(CountPauseWarnings(playing), Is.EqualTo(1), string.Join(" | ", playing.Warnings));
            Assert.That(
                playing.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            Assert.That(CountPauseWarnings(notPlaying), Is.Zero, string.Join(" | ", notPlaying.Warnings));
            Assert.That(CountPauseWarnings(playingWithoutFields), Is.Zero);
            Assert.That(
                playingWithoutFields.Message,
                Does.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage),
                "Precondition: without a field to wire, the two cleared-by-compile warnings keep the sentence.");
        }

        /// <summary>
        /// What: a run in unpaused Play Mode whose serialized added field warning names a field
        /// gets the pause warning even with no field to rewire after a reload or revert, and the
        /// warning, being a caller action, drops the single-compile sentence the same result keeps
        /// outside Play Mode.
        /// </summary>
        [Test]
        public void Build_WhilePlayingWithReportedSerializedField_AddsThePauseWarning()
        {
            HotReloadResponse response = Build(
                CreatePatchedResultWithTwoWarnings(new[] { SerializedFieldName }),
                Array.Empty<string>(),
                isPlaying: true);
            HotReloadResponse notPlaying = Build(
                CreatePatchedResultWithTwoWarnings(new[] { SerializedFieldName }),
                Array.Empty<string>(),
                isPlaying: false);

            Assert.That(CountPauseWarnings(response), Is.EqualTo(1), string.Join(" | ", response.Warnings));
            Assert.That(
                response.Message,
                Does.Not.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage));
            Assert.That(
                notPlaying.Message,
                Does.Contain(HotReloadConstants.MultiWarningSingleCompileResolutionMessage),
                "Precondition: outside Play Mode the same result keeps the single-compile sentence.");
        }

        private static HotReloadResponse Build(
            HotReloadOrchestratorResult result,
            IReadOnlyList<string> rewireFields,
            bool isPlaying)
        {
            return HotReloadApplyResponseBuilder.Build(
                HotReloadCompositionRoot.Services,
                result,
                Array.Empty<string>(),
                rewireFields,
                isPlaying,
                isPaused: false);
        }

        private static int CountPauseWarnings(HotReloadResponse response)
        {
            return response.Warnings.Count(
                entry => entry == HotReloadConstants.PauseBeforeWiringDuringPlayWarning);
        }

        private static HotReloadOrchestratorResult CreatePatchedResultWithTwoWarnings(
            IReadOnlyList<string> serializedAddedFieldsReported)
        {
            return new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Ns.Host.Tick()", "Assets/Host.cs")
                },
                new List<string> { "warn-a", "warn-b" },
                patchedTotal: 1,
                activePatchTotal: 1,
                addedFields: new[] { "Ns.Host.Speed" },
                serializedAddedFieldsReported: serializedAddedFieldsReported);
        }
    }
}
