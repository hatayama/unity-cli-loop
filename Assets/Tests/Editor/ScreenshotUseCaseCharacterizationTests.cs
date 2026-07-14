#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterization tests that lock ScreenshotUseCase pure helpers before god-class splits.
    /// </summary>
    public class ScreenshotUseCaseCharacterizationTests
    {
        /// <summary>
        /// Pins timeout response shaping including TimedOut flag and wait-name message text.
        /// </summary>
        [Test]
        public void CreateTimedOutResult_WhenCalled_ShouldMarkTimedOutAndIncludeWaitName()
        {
            List<ScreenshotInfo> screenshots = new()
            {
                new ScreenshotInfo { ImagePath = "a.png" }
            };

            ScreenshotResponse response = ScreenshotUseCase.CreateTimedOutResult(
                "raycast grid rendering info capture",
                "corr-1",
                screenshots);

            Assert.That(response.TimedOut, Is.True);
            Assert.That(response.Screenshots, Is.SameAs(screenshots));
            Assert.That(
                response.Message,
                Is.EqualTo(
                    $"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for raycast grid rendering info capture frames."));
        }

        /// <summary>
        /// Pins rendering-mode coordinate metadata fields written onto ScreenshotInfo.
        /// </summary>
        [Test]
        public void ApplyRenderingCoordinateMetadata_WhenCalled_ShouldSetGameViewFormulas()
        {
            ScreenshotInfo info = new();

            ScreenshotUseCase.ApplyRenderingCoordinateMetadata(info, new Vector2(1920f, 1080f), 24);

            Assert.That(info.ImageCoordinateSystem, Is.EqualTo(UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW));
            Assert.That(info.GameViewWidth, Is.EqualTo(1920f));
            Assert.That(info.GameViewHeight, Is.EqualTo(1080f));
            Assert.That(info.ImageToInputOffsetY, Is.EqualTo(24));
            Assert.That(info.ScreenshotToInputFormula, Is.EqualTo(UnityCliLoopConstants.SCREENSHOT_RENDERING_TO_INPUT_FORMULA));
            Assert.That(info.UnityInputFormula, Is.EqualTo(UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY));
        }

        /// <summary>
        /// Pins window-mode coordinate metadata fields written onto ScreenshotInfo.
        /// </summary>
        [Test]
        public void ApplyWindowCoordinateMetadata_WhenCalled_ShouldSetWindowFormulas()
        {
            ScreenshotInfo info = new();

            ScreenshotUseCase.ApplyWindowCoordinateMetadata(info);

            Assert.That(info.ImageCoordinateSystem, Is.EqualTo(UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_WINDOW));
            Assert.That(info.ScreenshotToInputFormula, Is.EqualTo(UnityCliLoopConstants.SCREENSHOT_WINDOW_TO_INPUT_FORMULA_UNAVAILABLE));
            Assert.That(info.UnityInputFormula, Is.EqualTo(""));
        }

        /// <summary>
        /// Pins response annotated-element concatenation order (UI first, then physics).
        /// </summary>
        [Test]
        public void CreateResponseAnnotatedElements_WhenBothListsProvided_ShouldConcatenateUiThenPhysics()
        {
            List<UIElementInfo> uiElements = new()
            {
                new UIElementInfo { Label = "A" }
            };
            List<UIElementInfo> physicsElements = new()
            {
                new UIElementInfo { Label = "P1" },
                new UIElementInfo { Label = "P2" }
            };

            List<UIElementInfo> combined = ScreenshotUseCase.CreateResponseAnnotatedElements(
                uiElements,
                physicsElements);

            Assert.That(combined, Has.Count.EqualTo(3));
            Assert.That(combined[0].Label, Is.EqualTo("A"));
            Assert.That(combined[1].Label, Is.EqualTo("P1"));
            Assert.That(combined[2].Label, Is.EqualTo("P2"));
            Assert.That(combined, Is.Not.SameAs(uiElements));
        }

        /// <summary>
        /// Pins invalid RaycastLayerMask message text including invalid and valid layer names.
        /// </summary>
        [Test]
        public void CreateInvalidRaycastLayerMaskMessage_WhenInvalidNamesExist_ShouldListInvalidAndValidLayers()
        {
            RaycastLayerMaskResolution resolution = new()
            {
                InvalidLayerNames = new List<string> { "Missing", "AlsoMissing" },
                ValidLayerNames = new List<string> { "Default", "UI" }
            };

            string message = ScreenshotUseCase.CreateInvalidRaycastLayerMaskMessage(resolution);

            Assert.That(
                message,
                Is.EqualTo(
                    "RaycastLayerMask contains unknown layer name(s): Missing, AlsoMissing. Valid layers: Default, UI"));
        }

        /// <summary>
        /// Pins empty valid-layer list rendering as (none) in the invalid-mask message.
        /// </summary>
        [Test]
        public void CreateInvalidRaycastLayerMaskMessage_WhenNoValidLayers_ShouldRenderNonePlaceholder()
        {
            RaycastLayerMaskResolution resolution = new()
            {
                InvalidLayerNames = new List<string> { "Missing" },
                ValidLayerNames = new List<string>()
            };

            string message = ScreenshotUseCase.CreateInvalidRaycastLayerMaskMessage(resolution);

            Assert.That(
                message,
                Is.EqualTo(
                    "RaycastLayerMask contains unknown layer name(s): Missing. Valid layers: (none)"));
        }

        /// <summary>
        /// Pins file-name sanitization replacing platform-invalid path characters with underscores.
        /// </summary>
        [Test]
        public void SanitizeFileName_WhenNameContainsInvalidChars_ShouldReplaceWithUnderscore()
        {
            // why: on this Unity/macOS runtime Path.GetInvalidFileNameChars is only NUL and '/', so ':' stays
            string sanitized = ScreenshotUseCase.SanitizeFileName("Game/View:Main");

            Assert.That(sanitized, Is.EqualTo("Game_View:Main"));
        }
    }
}
