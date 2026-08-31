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

            ScreenshotResponse response = ScreenshotCaptureResults.CreateTimedOutResult(
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

            ScreenshotCaptureResults.ApplyRenderingCoordinateMetadata(info, new Vector2(1920f, 1080f), 24);

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

            ScreenshotCaptureResults.ApplyWindowCoordinateMetadata(info);

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

            List<UIElementInfo> combined = ScreenshotCaptureResults.CreateResponseAnnotatedElements(
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

            string message = ScreenshotCaptureResults.CreateInvalidRaycastLayerMaskMessage(resolution);

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

            string message = ScreenshotCaptureResults.CreateInvalidRaycastLayerMaskMessage(resolution);

            Assert.That(
                message,
                Is.EqualTo(
                    "RaycastLayerMask contains unknown layer name(s): Missing. Valid layers: (none)"));
        }

        /// <summary>
        /// Pins ElementsOnly SimY conversion to the measured GameViewSize.y rather than a stale sample.
        /// </summary>
        [Test]
        public void BuildElementsOnlyScreenshotInfo_WhenRenderingInfoIsProvided_ShouldFlipSimYWithMeasuredHeight()
        {
            List<UIElementInfo> annotatedElements = new()
            {
                new UIElementInfo
                {
                    Name = "Button_CenterBlocked",
                    SimX = 100f,
                    SimY = 100f,
                    BoundsMinX = 50f,
                    BoundsMinY = 80f,
                    BoundsMaxX = 150f,
                    BoundsMaxY = 120f
                }
            };
            GameRenderingImageInfo renderingInfo = new(new Vector2(800f, 600f), new Vector2(800f, 558f), 42);

            ScreenshotResponse response = ScreenshotCaptureResults.BuildElementsOnlyScreenshotInfo(
                annotatedElements,
                new List<UIElementInfo>(),
                new List<RaycastLayerSummaryInfo>(),
                new List<string>(),
                1f,
                renderingInfo);

            UIElementInfo element = response.Screenshots[0].AnnotatedElements[0];
            Assert.That(element.SimY, Is.EqualTo(500f));
            Assert.That(element.BoundsMinY, Is.EqualTo(480f));
            Assert.That(element.BoundsMaxY, Is.EqualTo(520f));
        }

        /// <summary>
        /// Pins ElementsOnly GameViewWidth/Height metadata to the measured GameViewSize.
        /// </summary>
        [Test]
        public void BuildElementsOnlyScreenshotInfo_WhenRenderingInfoIsProvided_ShouldCopyGameViewSizeIntoMetadata()
        {
            GameRenderingImageInfo renderingInfo = new(new Vector2(800f, 600f), new Vector2(800f, 558f), 42);

            ScreenshotResponse response = ScreenshotCaptureResults.BuildElementsOnlyScreenshotInfo(
                new List<UIElementInfo>(),
                new List<UIElementInfo>(),
                new List<RaycastLayerSummaryInfo>(),
                new List<string>(),
                1f,
                renderingInfo);

            ScreenshotInfo info = response.Screenshots[0];
            Assert.That(info.GameViewWidth, Is.EqualTo(800f));
            Assert.That(info.GameViewHeight, Is.EqualTo(600f));
        }

        /// <summary>
        /// Pins ElementsOnly ImageToInputOffsetY metadata to the measured rendering info.
        /// </summary>
        [Test]
        public void BuildElementsOnlyScreenshotInfo_WhenRenderingInfoIsProvided_ShouldCopyImageToInputOffsetY()
        {
            GameRenderingImageInfo renderingInfo = new(new Vector2(800f, 600f), new Vector2(800f, 558f), 42);

            ScreenshotResponse response = ScreenshotCaptureResults.BuildElementsOnlyScreenshotInfo(
                new List<UIElementInfo>(),
                new List<UIElementInfo>(),
                new List<RaycastLayerSummaryInfo>(),
                new List<string>(),
                0.5f,
                renderingInfo);

            Assert.That(response.Screenshots[0].ImageToInputOffsetY, Is.EqualTo(42));
            Assert.That(response.Screenshots[0].ResolutionScale, Is.EqualTo(0.5f));
        }

        /// <summary>
        /// Pins file-name sanitization replacing the fixed cross-platform invalid set with underscores.
        /// </summary>
        [Test]
        public void SanitizeFileName_WhenNameContainsInvalidChars_ShouldReplaceWithUnderscore()
        {
            string sanitized = ScreenshotCaptureResults.SanitizeFileName("Game/View:Main");

            Assert.That(sanitized, Is.EqualTo("Game_View_Main"));
        }
    }
}
