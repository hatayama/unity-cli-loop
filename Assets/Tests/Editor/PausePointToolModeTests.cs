using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies pause point tool mode parameters and response history mapping.
    /// </summary>
    [TestFixture]
    public sealed class PausePointToolModeTests
    {
        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies mode and max-history JSON parameters reach the enable response.
        /// </summary>
        [Test]
        public async Task Enable_WhenModeAndMaxHistoryAreProvided_MapsParameters()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30,
                ["mode"] = UloopPausePointCaptureMode.Continuous,
                ["maxHistory"] = 2
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Mode, Is.EqualTo(UloopPausePointCaptureMode.Continuous));
            Assert.That(response.MaxHistory, Is.EqualTo(2));
        }

        /// <summary>
        /// What: an id-only marker rejects hit-when because it has no captured method variables.
        /// </summary>
        [Test]
        public async Task Enable_WhenIdMarkerUsesHitWhen_ReturnsSourceMarkerValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["hitWhen"] = "speed > 5"
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("--hit-when requires a --file/--line marker."));
        }

        /// <summary>
        /// What: malformed hit-when input returns the DSL grammar error before a marker is armed.
        /// </summary>
        [Test]
        public async Task Enable_WhenHitWhenIsMalformed_ReturnsDslGrammarValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = "Assets/NoSuchHitWhenFixture.cs",
                ["line"] = 1,
                ["hitWhen"] = "speed matches 5"
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo("--hit-when must use '<name> <op> <literal>' where name is an identifier or this and op is ==, !=, >, >=, <, or <=."));
        }

        /// <summary>
        /// Verifies an unknown mode returns a user-facing validation failure with supported values.
        /// </summary>
        [Test]
        public async Task Enable_WhenModeIsUnknown_ReturnsSupportedModeValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = "unknown",
                ["maxHistory"] = 20
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Mode must be one of: single-shot, continuous, trace."));
        }

        /// <summary>
        /// Verifies an out-of-range max-history value returns a user-facing validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxHistoryIsOutOfRange_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = UloopPausePointCaptureMode.SingleShot,
                ["maxHistory"] = UloopPausePointRegistry.MaxHistoryLimit + 1
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxHistory must be between 1 and 100."));
        }

        /// <summary>
        /// Verifies a non-positive max-history value returns a validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxHistoryIsZero_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["mode"] = UloopPausePointCaptureMode.SingleShot,
                ["maxHistory"] = 0
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxHistory must be between 1 and 100."));
        }

        /// <summary>
        /// Verifies the max-preview-elements JSON parameter reaches the enable response.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxPreviewElementsIsProvided_MapsParameter()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30,
                ["maxPreviewElements"] = 200
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.MaxPreviewElements, Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies max-preview-elements defaults to the registry default when omitted.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxPreviewElementsIsOmitted_DefaultsToRegistryDefault()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.MaxPreviewElements, Is.EqualTo(UloopPausePointRegistry.DefaultMaxPreviewElements));
        }

        /// <summary>
        /// Verifies an out-of-range max-preview-elements value returns a user-facing validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxPreviewElementsIsOutOfRange_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["maxPreviewElements"] = UloopPausePointRegistry.MaxPreviewElementsLimit + 1
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxPreviewElements must be between 1 and 1000."));
        }

        /// <summary>
        /// Verifies a non-positive max-preview-elements value returns a validation failure.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxPreviewElementsIsZero_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["maxPreviewElements"] = 0
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxPreviewElements must be between 1 and 1000."));
        }

        /// <summary>
        /// Verifies a source pause point hit serializes its collection preview using the marker's
        /// own max-preview-elements override instead of the fixed default.
        /// </summary>
        [Test]
        public void Hit_WhenMarkerHasMaxPreviewElementsOverride_UsesOverrideForCollectionPreview()
        {
            // Verifies the Harmony entry point itself (SourcePausePointCapture.Capture) resolves
            // the marker's override through UloopPausePointRegistry.GetMaxPreviewElements, not
            // just that CaptureFrame honors a value handed to it directly.
            const int maxPreviewElements = 3;
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.SingleShot, 20, maxPreviewElements);
            List<int> values = Enumerable.Range(0, maxPreviewElements + 5).ToList();

            SourcePausePointCapture.Capture(
                "jump", null, Array.Empty<object>(), new object[] { "scores", values });

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.CapturedVariables.Single().Value.Split(',').Length, Is.EqualTo(maxPreviewElements));
            Assert.That(snapshot.CapturedVariablesTruncated, Is.True);
        }

        /// <summary>
        /// What: a false hit-when condition skips capture before it can add a trace-history frame.
        /// </summary>
        [Test]
        public void Capture_WhenHitWhenDoesNotMatch_SkipsCaptureAndRecordsSkipCount()
        {
            UloopPausePointHitWhenParseResult parseResult = UloopPausePointHitWhenCondition.Parse("speed > 5");
            UloopPausePointRegistry.Enable(
                "jump",
                30,
                UloopPausePointCaptureMode.Trace,
                20,
                UloopPausePointRegistry.DefaultMaxPreviewElements,
                UloopPausePointRegistry.DefaultMaxCallerFrames,
                "speed > 5",
                parseResult.Condition);

            SourcePausePointCapture.Capture(
                "jump", null, Array.Empty<object>(), new object[] { "speed", 5 });

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.HitCount, Is.EqualTo(0));
            Assert.That(snapshot.HitWhen, Is.EqualTo("speed > 5"));
            Assert.That(snapshot.HitWhenSkippedCount, Is.EqualTo(1));
            Assert.That(snapshot.HitWhenErrorNote, Is.EqualTo(string.Empty));
            Assert.That(snapshot.CapturedVariableHistory, Is.Empty);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a hit-when type error records the first error and keeps the capture fail-open.
        /// </summary>
        [Test]
        public void Capture_WhenHitWhenEvaluationErrors_RecordsErrorAndCapturesFrame()
        {
            UloopPausePointHitWhenParseResult parseResult = UloopPausePointHitWhenCondition.Parse("speed == true");
            UloopPausePointRegistry.Enable(
                "jump",
                30,
                UloopPausePointCaptureMode.Trace,
                20,
                UloopPausePointRegistry.DefaultMaxPreviewElements,
                UloopPausePointRegistry.DefaultMaxCallerFrames,
                "speed == true",
                parseResult.Condition);

            SourcePausePointCapture.Capture(
                "jump", null, Array.Empty<object>(), new object[] { "speed", 5 });

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.HitCount, Is.EqualTo(1));
            Assert.That(snapshot.HitWhenSkippedCount, Is.EqualTo(0));
            Assert.That(snapshot.HitWhenErrorNote, Is.EqualTo("--hit-when expected variable 'speed' to be Boolean."));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// What: the first hit-when evaluation error remains status evidence when later frames fail differently.
        /// </summary>
        [Test]
        public void RecordHitWhenError_WhenMultipleErrorsAreReported_KeepsFirstError()
        {
            UloopPausePointHitWhenParseResult parseResult = UloopPausePointHitWhenCondition.Parse("speed > 5");
            UloopPausePointRegistry.Enable(
                "jump",
                30,
                UloopPausePointCaptureMode.Trace,
                20,
                UloopPausePointRegistry.DefaultMaxPreviewElements,
                UloopPausePointRegistry.DefaultMaxCallerFrames,
                "speed > 5",
                parseResult.Condition);

            UloopPausePointRegistry.RecordHitWhenError("jump", "first error");
            UloopPausePointRegistry.RecordHitWhenError("jump", "later error");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.HitWhenErrorNote, Is.EqualTo("first error"));
        }

        /// <summary>
        /// Verifies GetMaxPreviewElements resolves the value stored by Enable for an armed marker.
        /// </summary>
        [Test]
        public void GetMaxPreviewElements_WhenMarkerIsEnabled_ReturnsConfiguredValue()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.SingleShot, 20, 200);

            Assert.That(UloopPausePointRegistry.GetMaxPreviewElements("jump"), Is.EqualTo(200));
        }

        /// <summary>
        /// Verifies GetMaxPreviewElements falls back to the registry default for an unknown id.
        /// </summary>
        [Test]
        public void GetMaxPreviewElements_WhenMarkerIsUnknown_ReturnsRegistryDefault()
        {
            Assert.That(
                UloopPausePointRegistry.GetMaxPreviewElements("unknown"),
                Is.EqualTo(UloopPausePointRegistry.DefaultMaxPreviewElements));
        }

        /// <summary>
        /// What: max-caller-frames 0, 1, and 8 reach the enable response.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        public async Task Enable_WhenMaxCallerFramesIsInRange_MapsParameter(int maxCallerFrames)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30,
                ["maxCallerFrames"] = maxCallerFrames
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.MaxCallerFrames, Is.EqualTo(maxCallerFrames));
        }

        /// <summary>
        /// What: max-caller-frames defaults to 2 when omitted.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxCallerFramesIsOmitted_DefaultsToTwo()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.MaxCallerFrames, Is.EqualTo(UloopPausePointRegistry.DefaultMaxCallerFrames));
        }

        /// <summary>
        /// What: max-caller-frames 9 is rejected as out of range.
        /// </summary>
        [Test]
        public async Task Enable_WhenMaxCallerFramesIsNine_ReturnsValidationFailure()
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["maxCallerFrames"] = 9
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("MaxCallerFrames must be between 0 and 8."));
        }

        /// <summary>
        /// What: GetMaxCallerFrames returns the per-marker cap stored at Enable.
        /// </summary>
        [Test]
        public void GetMaxCallerFrames_WhenMarkerIsEnabled_ReturnsConfiguredValue()
        {
            UloopPausePointRegistry.Enable(
                "jump", 30, UloopPausePointCaptureMode.SingleShot, 20, 10, 4);

            Assert.That(UloopPausePointRegistry.GetMaxCallerFrames("jump"), Is.EqualTo(4));
        }

        /// <summary>
        /// What: GetMaxCallerFrames falls back to the default for an unknown id.
        /// </summary>
        [Test]
        public void GetMaxCallerFrames_WhenMarkerIsUnknown_ReturnsRegistryDefault()
        {
            Assert.That(
                UloopPausePointRegistry.GetMaxCallerFrames("unknown"),
                Is.EqualTo(UloopPausePointRegistry.DefaultMaxCallerFrames));
        }

        /// <summary>
        /// What: a hit with max-caller-frames 0 still carries an empty CallerFrames array.
        /// </summary>
        [Test]
        public void Hit_WhenMaxCallerFramesIsZero_ReturnsEmptyCallerFramesArray()
        {
            UloopPausePointRegistry.Enable(
                "jump", 30, UloopPausePointCaptureMode.SingleShot, 20, 10, 0);

            SourcePausePointCapture.Capture(
                "jump", null, Array.Empty<object>(), Array.Empty<object>());

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.CallerFrames, Is.Empty);
        }

        /// <summary>
        /// Verifies the CLI-only status bridge exposes mode and captured history fields.
        /// </summary>
        [Test]
        public void StatusBridge_WhenContinuousMarkerHasHit_ReturnsHistoryFields()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);
            UloopPausePointRegistry.Hit("jump");

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(
                new JObject { ["id"] = "jump" });

            Assert.That(response.Mode, Is.EqualTo(UloopPausePointCaptureMode.Continuous));
            Assert.That(response.MaxHistory, Is.EqualTo(20));
            Assert.That(response.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(response.HistoryDroppedCount, Is.EqualTo(0));
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
