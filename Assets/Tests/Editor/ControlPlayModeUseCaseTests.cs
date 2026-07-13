using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Control Play Mode behavior without entering PlayMode.
    /// </summary>
    public sealed class ControlPlayModeUseCaseTests
    {
        [Test]
        public void ControlPlayModeSchema_WhenCreated_UsesToolReadinessSizedTimeout()
        {
            // Verifies that PlayMode waits default to the repository's long-running tool readiness window.
            ControlPlayModeSchema schema = new ControlPlayModeSchema();

            Assert.That(schema.TimeoutSeconds, Is.EqualTo(ControlPlayModeUseCase.DefaultTimeoutSeconds));
        }

        [Test]
        public async Task ExecuteAsync_WhenStatusOnly_ReturnsCurrentPlayModeState()
        {
            // Verifies that the CLI can inspect PlayMode state without changing it during post-reload waits.
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase();
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                StatusOnly = true,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo("Play mode status"));
        }

        [Test]
        public async Task ExecuteAsync_WhenStatusOnlyPlayBlockedByCompileErrors_ReturnsSavedDiagnostics()
        {
            // Verifies polling can report compiler diagnostics when PlayMode becomes blocked after the first request.
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeCompileError[] compileErrors =
            {
                new ControlPlayModeCompileError
                {
                    Message = "CS1525: invalid expression",
                    File = "Assets/Scripts/Sample.cs",
                    Line = 3
                }
            };
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(compileErrors),
                new StubCompilationFailureGate(true));
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Play,
                StatusOnly = true,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.BlockedByCompileErrors, Is.True);
            Assert.That(response.CompileErrorCount, Is.EqualTo(1));
            Assert.That(response.CompileErrors[0].Message, Is.EqualTo("CS1525: invalid expression"));
            Assert.That(response.Message, Is.EqualTo("Play mode could not start because Unity has compiler errors."));
        }

        [Test]
        public async Task ExecuteAsync_WhenStatusOnlyStopAndCompileFailed_ReturnsCurrentPlayModeState()
        {
            // Verifies compiler errors are only treated as a status polling blocker for Play requests.
            ControlPlayModeCompileError[] compileErrors =
            {
                new ControlPlayModeCompileError
                {
                    Message = "CS1525: invalid expression",
                    File = "Assets/Scripts/Sample.cs",
                    Line = 3
                }
            };
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(compileErrors),
                new StubCompilationFailureGate(true));
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Stop,
                StatusOnly = true,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.BlockedByCompileErrors, Is.False);
            Assert.That(response.Message, Is.EqualTo("Play mode status"));
        }

        [Test]
        public async Task ExecuteAsync_WhenStepOutsidePlayMode_ReturnsNoOpWithGuidance()
        {
            // Verifies Step refuses to run outside PlayMode instead of silently queuing a frame step.
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase();
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Step,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo("Play mode is not running. Step requires PlayMode; start it with --action Play first."));
            Assert.That(response.Changed, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_WhenStopAlreadyStopped_ReturnsNoOpState()
        {
            // Verifies Stop distinguishes a no-op from a state-changing PlayMode exit.
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase();
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Stop,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo("Play mode was already stopped"));
            Assert.That(response.Changed, Is.False);
            Assert.That(response.WasAlreadyStopped, Is.True);
        }

        [Test]
        public void CompilationFailureService_WhenCompilationFails_StoresCompilerErrors()
        {
            // Verifies saved PlayMode diagnostics come from the compiler failure snapshot.
            ControlPlayModeCompilationFailureService service = new ControlPlayModeCompilationFailureService();
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "CS1002: ; expected",
                file = "Assets/Scripts/Sample.cs",
                line = 12
            };
            CompilerMessage warning = new CompilerMessage
            {
                type = CompilerMessageType.Warning,
                message = "CS0168: variable declared but never used",
                file = "Assets/Scripts/Sample.cs",
                line = 8
            };

            service.HandleCompilationStarted(null);
            service.HandleAssemblyCompilationFinished("Assembly-CSharp.dll", new[] { error, warning });
            service.HandleCompilationFinished(null);

            ControlPlayModeCompileError[] errors = service.GetLastFailedErrors();
            Assert.That(errors.Length, Is.EqualTo(1));
            Assert.That(errors[0].Message, Is.EqualTo("CS1002: ; expected"));
            Assert.That(errors[0].File, Is.EqualTo("Assets/Scripts/Sample.cs"));
            Assert.That(errors[0].Line, Is.EqualTo(12));
        }

        [Test]
        public void CompilationFailureService_WhenNextCompilationStarts_ClearsPreviousErrors()
        {
            // Verifies stale compiler diagnostics are discarded as soon as Unity starts compiling again.
            ControlPlayModeCompilationFailureService service = new ControlPlayModeCompilationFailureService();
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "CS0103: name does not exist",
                file = "Assets/Scripts/Sample.cs",
                line = 3
            };

            service.HandleCompilationStarted(null);
            service.HandleAssemblyCompilationFinished("Assembly-CSharp.dll", new[] { error });
            service.HandleCompilationFinished(null);
            service.HandleCompilationStarted(null);

            ControlPlayModeCompileError[] errors = service.GetLastFailedErrors();
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void CompilationFailureService_WhenCompilationSucceeds_ClearsPreviousErrors()
        {
            // Verifies successful compilation removes the previous failure snapshot.
            ControlPlayModeCompilationFailureService service = new ControlPlayModeCompilationFailureService();
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "CS0246: type not found",
                file = "Assets/Scripts/Sample.cs",
                line = 5
            };

            service.HandleCompilationStarted(null);
            service.HandleAssemblyCompilationFinished("Assembly-CSharp.dll", new[] { error });
            service.HandleCompilationFinished(null);
            service.HandleCompilationStarted(null);
            service.HandleAssemblyCompilationFinished("Assembly-CSharp.dll", System.Array.Empty<CompilerMessage>());
            service.HandleCompilationFinished(null);

            ControlPlayModeCompileError[] errors = service.GetLastFailedErrors();
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_WhenPlayBlockedByCompileErrors_ReturnsSavedDiagnostics()
        {
            // Verifies Play returns compiler diagnostics immediately instead of waiting for a state timeout.
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeCompileError[] compileErrors =
            {
                new ControlPlayModeCompileError
                {
                    Message = "CS1002: ; expected",
                    File = "Assets/Scripts/Sample.cs",
                    Line = 12
                }
            };
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(compileErrors),
                new StubCompilationFailureGate(true));
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Play,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.BlockedByCompileErrors, Is.True);
            Assert.That(response.Changed, Is.False);
            Assert.That(response.IsPlaying, Is.False);
            Assert.That(response.CompileErrorCount, Is.EqualTo(1));
            Assert.That(response.CompileErrors[0].Message, Is.EqualTo("CS1002: ; expected"));
        }

        [Test]
        public async Task ExecuteAsync_WhenPlayBlockedWithoutSavedErrors_ReturnsEmptyDiagnostics()
        {
            // Verifies Play still fails fast when Unity reports compilation failure but no snapshot is available.
            Assert.That(EditorApplication.isPlaying, Is.False);
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(System.Array.Empty<ControlPlayModeCompileError>()),
                new StubCompilationFailureGate(true));
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Play,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.BlockedByCompileErrors, Is.True);
            Assert.That(response.CompileErrorCount, Is.EqualTo(0));
            Assert.That(response.CompileErrors, Is.Empty);
            Assert.That(response.Message, Does.Contain("no saved compiler diagnostics"));
        }

        [Test]
        public async Task ExecuteAsync_WhenPlayStartSaveFails_DoesNotEnterPlayMode()
        {
            // Verifies dirty Scene/Prefab save failure blocks Edit→Play instead of prompting or hanging.
            Assert.That(EditorApplication.isPlaying, Is.False);
            StubEditorUnsavedChangesQuietSaver quietSaver = new(
                saveFailures: new[] { "Scene: Assets/Scenes/Sample.unity" },
                remainingAfterSave: System.Array.Empty<string>());
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(System.Array.Empty<ControlPlayModeCompileError>()),
                new StubCompilationFailureGate(false),
                quietSaver);
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Play,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(quietSaver.SaveCallCount, Is.EqualTo(1));
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(response.Changed, Is.False);
            Assert.That(response.IsPlaying, Is.False);
            Assert.That(response.Message, Does.Contain("could not be saved"));
            Assert.That(response.Message, Does.Contain("Scene: Assets/Scenes/Sample.unity"));
        }

        [Test]
        public async Task ExecuteAsync_WhenPlayStartLeavesUnsavedChanges_DoesNotEnterPlayMode()
        {
            // Verifies remaining dirty editor state after a quiet save still blocks Play start.
            Assert.That(EditorApplication.isPlaying, Is.False);
            StubEditorUnsavedChangesQuietSaver quietSaver = new(
                saveFailures: System.Array.Empty<string>(),
                remainingAfterSave: new[] { "Prefab Stage: Assets/Prefabs/Hud.prefab" });
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase(
                new StubCompilationFailureProvider(System.Array.Empty<ControlPlayModeCompileError>()),
                new StubCompilationFailureGate(false),
                quietSaver);
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                Action = PlayModeAction.Play,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(quietSaver.SaveCallCount, Is.EqualTo(1));
            Assert.That(quietSaver.DetectCallCount, Is.EqualTo(1));
            Assert.That(EditorApplication.isPlaying, Is.False);
            Assert.That(response.Changed, Is.False);
            Assert.That(response.Message, Does.Contain("unsaved scene or prefab changes"));
            Assert.That(response.Message, Does.Contain("Prefab Stage: Assets/Prefabs/Hud.prefab"));
        }

        private sealed class StubCompilationFailureProvider : IControlPlayModeCompilationFailureProvider
        {
            private readonly ControlPlayModeCompileError[] _errors;

            public StubCompilationFailureProvider(ControlPlayModeCompileError[] errors)
            {
                _errors = errors;
            }

            public ControlPlayModeCompileError[] GetLastFailedErrors()
            {
                return _errors;
            }
        }

        private sealed class StubCompilationFailureGate : IControlPlayModeCompilationFailureGate
        {
            private readonly bool _hasScriptCompilationFailed;

            public StubCompilationFailureGate(bool hasScriptCompilationFailed)
            {
                _hasScriptCompilationFailed = hasScriptCompilationFailed;
            }

            public bool HasScriptCompilationFailed()
            {
                return _hasScriptCompilationFailed;
            }
        }

        private sealed class StubEditorUnsavedChangesQuietSaver : IEditorUnsavedChangesQuietSaver
        {
            private readonly string[] _saveFailures;
            private readonly string[] _remainingAfterSave;

            public int SaveCallCount { get; private set; }
            public int DetectCallCount { get; private set; }

            public StubEditorUnsavedChangesQuietSaver(string[] saveFailures, string[] remainingAfterSave)
            {
                _saveFailures = saveFailures;
                _remainingAfterSave = remainingAfterSave;
            }

            public string[] DetectUnsavedEditorChanges()
            {
                DetectCallCount++;
                return _remainingAfterSave;
            }

            public string[] SaveUnsavedEditorChanges()
            {
                SaveCallCount++;
                return _saveFailures;
            }
        }
    }
}
