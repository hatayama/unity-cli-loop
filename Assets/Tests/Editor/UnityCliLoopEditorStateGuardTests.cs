using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the editor state guard turns busy readiness decisions into busy exceptions.
    /// </summary>
    public sealed class UnityCliLoopEditorStateGuardTests
    {
        /// <summary>
        /// Verifies a guarded tool requested during compilation is rejected with the compile operation and current editor state.
        /// </summary>
        [Test]
        public void Validate_WhenGuardedToolRequestedWhileCompiling_ThrowsBusyForUnityCompile()
        {
            ScriptedEditorRuntimeStatePort editorState = new ScriptedEditorRuntimeStatePort(true, false, true, false);

            UnityCliLoopToolBusyException exception = Assert.Throws<UnityCliLoopToolBusyException>(
                () => UnityCliLoopEditorStateGuard.Validate(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, editorState));

            Assert.That(exception.RunningToolName, Is.EqualTo("unity-compile"));
            Assert.That(exception.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(exception.IsCompiling, Is.True);
            Assert.That(exception.IsUpdating, Is.False);
            Assert.That(exception.IsPlaying, Is.True);
            Assert.That(exception.IsPaused, Is.False);
        }

        /// <summary>
        /// Verifies a guarded tool requested while the editor is idle passes validation.
        /// </summary>
        [Test]
        public void Validate_WhenGuardedToolRequestedWhileIdle_DoesNotThrow()
        {
            ScriptedEditorRuntimeStatePort editorState = new ScriptedEditorRuntimeStatePort(false, false, false, false);

            Assert.That(
                () => UnityCliLoopEditorStateGuard.Validate(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, editorState),
                Throws.Nothing);
        }

        /// <summary>
        /// Test support type that reports a fixed editor runtime state.
        /// </summary>
        private sealed class ScriptedEditorRuntimeStatePort : IEditorRuntimeStatePort
        {
            public ScriptedEditorRuntimeStatePort(bool isCompiling, bool isUpdating, bool isPlaying, bool isPaused)
            {
                IsCompiling = isCompiling;
                IsUpdating = isUpdating;
                IsPlaying = isPlaying;
                IsPaused = isPaused;
            }

            public bool IsCompiling { get; }
            public bool IsUpdating { get; }
            public bool IsPlaying { get; }
            public bool IsPaused { get; }
        }
    }
}
