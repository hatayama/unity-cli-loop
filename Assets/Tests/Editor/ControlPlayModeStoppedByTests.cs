using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that control-play-mode copies confirmed stop reasons onto Stop and Status responses.
    /// </summary>
    public sealed class ControlPlayModeStoppedByTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: Stop while already stopped copies the confirmed SessionState reason onto the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStopAlreadyStoppedAndReasonConfirmed_CopiesStoppedByAndStoppedAt()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-01T00:00:00.0000000Z");
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                compilationFailureProvider: new EmptyCompilationFailureProvider(),
                compilationFailureGate: new OpenCompilationFailureGate());
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Stop
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.WasAlreadyStopped, Is.True);
            Assert.That(response.StoppedBy, Is.EqualTo("cli-control-play-mode"));
            Assert.That(response.StoppedAt, Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
        }

        /// <summary>
        /// What: Status while Play Mode is stopped copies the confirmed SessionState reason onto the response.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStatusWhileStoppedAndReasonConfirmed_CopiesStoppedByAndStoppedAt()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            PlayModeStopReasonSessionStore.SetPending("cli-compile-stop-setting");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-03T00:00:00.0000000Z");
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                compilationFailureProvider: new EmptyCompilationFailureProvider(),
                compilationFailureGate: new OpenCompilationFailureGate());
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Status
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.IsPlaying, Is.False);
            Assert.That(response.StoppedBy, Is.EqualTo("cli-compile-stop-setting"));
            Assert.That(response.StoppedAt, Is.EqualTo("2026-01-03T00:00:00.0000000Z"));
        }

        /// <summary>
        /// What: Stop while already stopped omits StoppedBy when SessionState has no confirmed reason.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStopAlreadyStoppedAndNoRecord_LeavesStoppedByNull()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                compilationFailureProvider: new EmptyCompilationFailureProvider(),
                compilationFailureGate: new OpenCompilationFailureGate());
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Stop
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.WasAlreadyStopped, Is.True);
            Assert.That(response.StoppedBy, Is.Null);
            Assert.That(response.StoppedAt, Is.Null);
        }

        /// <summary>
        /// What: serialized JSON omits StoppedBy and StoppedAt when they are null, and includes them when set.
        /// </summary>
        [Test]
        public void ControlPlayModeResponse_WhenSerialized_OmitsNullStoppedByAndStoppedAtKeys()
        {
            ControlPlayModeResponse omitted = new ControlPlayModeResponse
            {
                Message = "Play mode was already stopped",
                Warning = string.Empty,
                CompileErrors = Array.Empty<ControlPlayModeCompileError>()
            };
            JObject omittedJson = JObject.Parse(
                JsonConvert.SerializeObject(
                    omitted,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            Assert.That(omittedJson.Property("StoppedBy"), Is.Null);
            Assert.That(omittedJson.Property("StoppedAt"), Is.Null);
            Assert.That(omittedJson.Property("Warning"), Is.Null);

            ControlPlayModeResponse populated = new ControlPlayModeResponse
            {
                Message = "Play mode was already stopped",
                Warning = string.Empty,
                CompileErrors = Array.Empty<ControlPlayModeCompileError>(),
                StoppedBy = "cli-control-play-mode",
                StoppedAt = "2026-01-01T00:00:00.0000000Z"
            };
            JObject populatedJson = LoadJsonWithoutDateParsing(
                JsonConvert.SerializeObject(
                    populated,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            Assert.That(populatedJson["StoppedBy"]?.Value<string>(), Is.EqualTo("cli-control-play-mode"));
            Assert.That(populatedJson["StoppedAt"]?.Value<string>(), Is.EqualTo("2026-01-01T00:00:00.0000000Z"));

            populated.Warning = "focus editor";
            JObject warningJson = LoadJsonWithoutDateParsing(
                JsonConvert.SerializeObject(
                    populated,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));
            Assert.That(warningJson["Warning"]?.Value<string>(), Is.EqualTo("focus editor"));
        }

        /// <summary>
        /// What: Stop with a changing stop does not copy StoppedBy even when a record exists.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenStopWhilePlaying_DoesNotCopyStoppedBy()
        {
            PlayModeStopReasonSessionStore.SetPending("script-compilation");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-04T00:00:00.0000000Z");
            FakePlayingEditorStateService editorState = new FakePlayingEditorStateService();
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                compilationFailureProvider: new EmptyCompilationFailureProvider(),
                compilationFailureGate: new OpenCompilationFailureGate(),
                editorStateService: editorState);
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Stop
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.WasAlreadyStopped, Is.False);
            Assert.That(response.StoppedBy, Is.Null);
            Assert.That(response.StoppedAt, Is.Null);
        }

        private static JObject LoadJsonWithoutDateParsing(string json)
        {
            using (StringReader stringReader = new StringReader(json))
            using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(jsonReader);
            }
        }

        private sealed class EmptyCompilationFailureProvider : IControlPlayModeCompilationFailureProvider
        {
            public ControlPlayModeCompileError[] GetLastFailedErrors()
            {
                return Array.Empty<ControlPlayModeCompileError>();
            }
        }

        private sealed class OpenCompilationFailureGate : IControlPlayModeCompilationFailureGate
        {
            public bool HasScriptCompilationFailed()
            {
                return false;
            }
        }

        private sealed class FakePlayingEditorStateService : IControlPlayModeEditorStateService
        {
            public bool IsPlaying { get; set; } = true;
            public bool IsPaused { get; set; }

            public void Step()
            {
            }
        }
    }
}
