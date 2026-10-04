#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the window-capture branch of ScreenshotUseCase with a fake capture service:
    /// window lookup, Simulator fallback, per-window saving, and the failure paths.
    /// </summary>
    public sealed class ScreenshotUseCaseWindowCaptureTests
    {
        private string _outputDirectory = "";

        [SetUp]
        public void SetUp()
        {
            // An existing input-visualization overlay makes the use case wait on real Editor frames.
            Assume.That(OverlayCanvasFactory.TryGetExisting(), Is.Null);
            _outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-screenshot-window-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, true);
            }
        }

        /// <summary>
        /// What: a missing non-default window returns a not-found failure without capturing anything.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenWindowIsMissing_ReturnsNotFound()
        {
            FakeWindowCaptureService captureService = new();
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Window 'Inspector' not found (MatchMode: exact)"));
            Assert.That(captureService.FindRequests, Is.EqualTo(new[] { "Inspector" }));
            Assert.That(captureService.CaptureCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a missing Game window retries the Simulator window and reports both missing when neither exists.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenGameAndSimulatorAreMissing_ReturnsSimulatorAwareNotFound()
        {
            FakeWindowCaptureService captureService = new();
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest(UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME),
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo("Neither Game nor Simulator window found; open the Game view or Device Simulator and retry"));
            Assert.That(
                captureService.FindRequests,
                Is.EqualTo(new[]
                {
                    UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                    UnityCliLoopConstants.SCREENSHOT_SIMULATOR_WINDOW_NAME
                }));
        }

        /// <summary>
        /// What: when only the Simulator window exists, the default Game request captures it under the Simulator name.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenOnlySimulatorExists_CapturesSimulatorWindow()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts[UnityCliLoopConstants.SCREENSHOT_SIMULATOR_WINDOW_NAME] = 1;
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest(UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME),
                CancellationToken.None));

            Assert.That(response.Screenshots.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(response.Screenshots[0].ImagePath), Does.StartWith("Simulator_"));
        }

        /// <summary>
        /// What: a single captured window is saved as one PNG named after the window, with its size reported.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenOneWindowIsCaptured_SavesPngAndReportsIt()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts["Inspector"] = 1;
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Screenshots.Count, Is.EqualTo(1));
            ScreenshotInfo info = response.Screenshots[0];
            Assert.That(File.Exists(info.ImagePath), Is.True);
            Assert.That(Path.GetDirectoryName(info.ImagePath), Is.EqualTo(Path.GetFullPath(_outputDirectory)));
            Assert.That(Path.GetFileName(info.ImagePath), Does.Match("^Inspector_[0-9]{8}_[0-9]{6}_[0-9]{3}\\.png$"));
            Assert.That(info.Width, Is.EqualTo(4));
            Assert.That(info.Height, Is.EqualTo(2));
            Assert.That(info.FileSizeBytes, Is.GreaterThan(0));
            Assert.That(captureService.CapturedTextures[0] == null, Is.True);
        }

        /// <summary>
        /// What: several matching windows are saved with a 1-based index in each file name.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenSeveralWindowsMatch_SavesIndexedFiles()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts["Inspector"] = 2;
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Screenshots.Count, Is.EqualTo(2));
            Assert.That(Path.GetFileName(response.Screenshots[0].ImagePath), Does.StartWith("Inspector_1_"));
            Assert.That(Path.GetFileName(response.Screenshots[1].ImagePath), Does.StartWith("Inspector_2_"));
        }

        /// <summary>
        /// What: a window whose capture returns no texture is skipped while the remaining windows are still saved.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenOneCaptureReturnsNoTexture_SkipsThatWindow()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts["Inspector"] = 2;
            captureService.NullTextureCaptureIndexes.Add(0);
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Screenshots.Count, Is.EqualTo(1));
            Assert.That(Path.GetFileName(response.Screenshots[0].ImagePath), Does.StartWith("Inspector_2_"));
        }

        /// <summary>
        /// What: a capture timeout returns the timed-out failure with the screenshots saved before it.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenCaptureTimesOut_ReturnsTimedOutResult()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts["Inspector"] = 2;
            captureService.TimedOutCaptureIndex = 1;
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.Contain("EditorWindow capture"));
            Assert.That(response.Screenshots.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a PNG that cannot be written is left out of the response and its texture is still destroyed.
        /// </summary>
        [Test]
        public async Task CaptureAsync_WhenSavingFails_OmitsScreenshotAndDestroysTexture()
        {
            FakeWindowCaptureService captureService = new();
            captureService.WindowCounts["Inspector"] = 1;
            // Why delete here: the use case creates the output directory before capturing, so removing it
            // afterwards makes the PNG write throw without depending on file-system permissions.
            captureService.BeforeReturningTexture = () => Directory.Delete(_outputDirectory, true);
            ScreenshotUseCase useCase = CreateUseCase(captureService);

            ScreenshotResponse response = await UncanceledAwaits.AwaitValueAsync(useCase.CaptureAsync(
                CreateRequest("Inspector"),
                CancellationToken.None));

            Assert.That(response.Screenshots, Is.Empty);
            Assert.That(captureService.CapturedTextures[0] == null, Is.True);
        }

        private ScreenshotUseCase CreateUseCase(FakeWindowCaptureService captureService)
        {
            return new ScreenshotUseCase(new EditModeStateReader(), captureService);
        }

        private ScreenshotSchema CreateRequest(string windowName)
        {
            return new ScreenshotSchema
            {
                CaptureMode = CaptureMode.window,
                WindowName = windowName,
                MatchMode = WindowMatchMode.exact,
                OutputDirectory = _outputDirectory
            };
        }

        private sealed class EditModeStateReader : IScreenshotEditorStateReader
        {
            public bool IsPlaying => false;
            public bool IsPaused => false;
        }

        /// <summary>
        /// Returns placeholder windows by name and small textures for each capture without touching real windows.
        /// </summary>
        private sealed class FakeWindowCaptureService : IEditorWindowCaptureService
        {
            public Dictionary<string, int> WindowCounts { get; } = new();
            public List<string> FindRequests { get; } = new();
            public HashSet<int> NullTextureCaptureIndexes { get; } = new();
            public int TimedOutCaptureIndex { get; set; } = -1;
            public Action? BeforeReturningTexture { get; set; }
            public List<Texture2D> CapturedTextures { get; } = new();
            public int CaptureCount { get; private set; }

            public EditorWindow[] FindWindowsByName(string windowName, WindowMatchMode matchMode)
            {
                FindRequests.Add(windowName);
                int count = WindowCounts.TryGetValue(windowName, out int value) ? value : 0;
                // Why null entries: the use case only forwards each window to CaptureWindowAsync.
                return new EditorWindow[count];
            }

            public Task<(Texture2D? texture, bool timedOut)> CaptureWindowAsync(
                EditorWindow window,
                float resolutionScale,
                int timeoutMilliseconds,
                CancellationToken ct)
            {
                int captureIndex = CaptureCount;
                CaptureCount++;
                if (captureIndex == TimedOutCaptureIndex)
                {
                    return Task.FromResult<(Texture2D?, bool)>((null, true));
                }

                if (NullTextureCaptureIndexes.Contains(captureIndex))
                {
                    return Task.FromResult<(Texture2D?, bool)>((null, false));
                }

                BeforeReturningTexture?.Invoke();
                Texture2D texture = new(4, 2, TextureFormat.RGBA32, false);
                CapturedTextures.Add(texture);
                return Task.FromResult<(Texture2D?, bool)>((texture, false));
            }

            public string[] GetOpenWindowNames()
            {
                return Array.Empty<string>();
            }

            public Task<(Texture2D? texture, int yOffset, bool timedOut)> CaptureGameRenderingAsync(
                float resolutionScale,
                int timeoutMilliseconds,
                CancellationToken ct)
            {
                throw new NotSupportedException("Window-capture tests never capture Game rendering.");
            }
        }
    }
}
