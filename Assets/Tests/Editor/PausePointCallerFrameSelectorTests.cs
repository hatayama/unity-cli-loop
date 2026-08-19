using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies caller-frame selection rules for pause-point hit captures.
    /// </summary>
    [TestFixture]
    public sealed class PausePointCallerFrameSelectorTests
    {
        private const string MarkerType = "Game.Player";
        private const string MarkerMethod = "Jump";
        private const string MarkerFile = "Assets/Scripts/Player.cs";
        private const int MarkerLine = 42;

        private const string UserType = "Game.Input";
        private const string UserMethod = "HandleJump";
        private const string UserFile = "Assets/Scripts/Input.cs";
        private const int UserLine = 10;

        /// <summary>
        /// What: rawFrames[0] is the marker's own frame and is skipped by position, so it
        /// never appears in the selected callers.
        /// </summary>
        [Test]
        public void Select_WhenMarkerIsFirstFrame_ExcludesMarkerFromResult()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(UserType, UserMethod, UserFile, UserLine),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo(UserType + "." + UserMethod));
            Assert.That(selected[0].File, Is.EqualTo(UserFile));
            Assert.That(selected[0].Line, Is.EqualTo(UserLine));
        }

        /// <summary>
        /// What: each infrastructure type prefix is skipped so deleting any one skip rule
        /// makes that case fail.
        /// </summary>
        [TestCase("System.Runtime.CompilerServices.AsyncTaskMethodBuilder")]
        [TestCase("Microsoft.CSharp.RuntimeBinder.Binder")]
        [TestCase("Mono.Cecil.Cil.ILProcessor")]
        [TestCase("HarmonyLib.Harmony")]
        [TestCase("MonoMod.RuntimeDetour.Hook")]
        [TestCase("io.github.hatayama.UnityCliLoop.FirstPartyTools.SourcePausePointCapture")]
        public void Select_WhenFrameTypeStartsWithSkippedPrefix_OmitsThatFrame(string skippedTypeFullName)
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(skippedTypeFullName, "Run", "Packages/Infrastructure/Run.cs", 7),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        /// <summary>
        /// What: UnityEditor frames stay because an editor entry point is itself diagnostic.
        /// </summary>
        [Test]
        public void Select_WhenFrameIsUnityEditorEditorApplication_KeepsTheFrame()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(
                    "UnityEditor.EditorApplication",
                    "update",
                    "Packages/UnityEditor/EditorApplication.cs",
                    120),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo("UnityEditor.EditorApplication.update"));
            Assert.That(selected[0].File, Is.EqualTo("Packages/UnityEditor/EditorApplication.cs"));
            Assert.That(selected[0].Line, Is.EqualTo(120));
        }

        /// <summary>
        /// What: a dynamic frame with no declaring type keeps its raw method name and reports
        /// no file or line instead of being silently dropped.
        /// </summary>
        [Test]
        public void Select_WhenTypeFullNameIsNull_KeepsRawMethodNameWithoutFileOrLine()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(null, "DMD<Foo>", null, 99),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo("DMD<Foo>"));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
        }

        /// <summary>
        /// What: selection stops at two user frames even when more callers remain.
        /// </summary>
        [Test]
        public void Select_WhenMoreThanTwoUserFramesExist_ReturnsAtMostTwo()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame("Game.A", "M1", "Assets/Scripts/A.cs", 1),
                CreateRawFrame("Game.B", "M2", "Assets/Scripts/B.cs", 2),
                CreateRawFrame("Game.C", "M3", "Assets/Scripts/C.cs", 3),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(selected[0].Method, Is.EqualTo("Game.A.M1"));
            Assert.That(selected[1].Method, Is.EqualTo("Game.B.M2"));
        }

        /// <summary>
        /// What: compiler-generated async state-machine MoveNext frames demangle to the
        /// logical Type.Method name.
        /// </summary>
        [Test]
        public void FormatMethodDisplay_WhenAsyncStateMachineMoveNext_ReturnsLogicalMethodName()
        {
            string display = SourcePausePointCallerFrameSelector.FormatMethodDisplay(
                "Ns.Type+<DoWork>d__3",
                "MoveNext");

            Assert.That(display, Is.EqualTo("Ns.Type.DoWork"));
        }

        /// <summary>
        /// What: ordinary methods are reported as TypeFullName.MethodName.
        /// </summary>
        [Test]
        public void FormatMethodDisplay_WhenOrdinaryMethod_ReturnsTypeDotMethod()
        {
            string display = SourcePausePointCallerFrameSelector.FormatMethodDisplay("Ns.Type", "Update");

            Assert.That(display, Is.EqualTo("Ns.Type.Update"));
        }

        /// <summary>
        /// What: a dynamic frame with no type or method name reports (unknown).
        /// </summary>
        [Test]
        public void FormatMethodDisplay_WhenTypeAndMethodAreNull_ReturnsUnknown()
        {
            string display = SourcePausePointCallerFrameSelector.FormatMethodDisplay(null, null);

            Assert.That(display, Is.EqualTo("(unknown)"));
        }

        /// <summary>
        /// What: Windows backslashes become forward slashes so the payload is stable across
        /// platforms.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenWindowsSeparators_ReturnsForwardSlashes()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "Assets\\Scripts\\Foo.cs");

            Assert.That(normalized, Is.EqualTo("Assets/Scripts/Foo.cs"));
        }

        /// <summary>
        /// What: Mono's leading ./ on script-assembly sources is stripped.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenLeadingDotSlash_StripsPrefix()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath("./Packages/Foo.cs");

            Assert.That(normalized, Is.EqualTo("Packages/Foo.cs"));
        }

        /// <summary>
        /// What: a missing file name normalizes to null rather than empty.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenFileNameIsNull_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(null);

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a frame without a file reports Line 0 even when the raw line number is stale.
        /// </summary>
        [Test]
        public void Select_WhenFileNameIsNull_ReportsLineZeroRegardlessOfRawLine()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(UserType, UserMethod, null, 42),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
        }

        /// <summary>
        /// What: an empty raw stack yields no callers.
        /// </summary>
        [Test]
        public void Select_WhenRawFramesAreEmpty_ReturnsEmptyList()
        {
            SourcePausePointRawStackFrame[] rawFrames = { };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        /// <summary>
        /// What: a stack that is only the marker frame yields no callers.
        /// </summary>
        [Test]
        public void Select_WhenOnlyMarkerFrameIsPresent_ReturnsEmptyList()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        private static SourcePausePointRawStackFrame CreateRawFrame(
            string typeFullName,
            string methodName,
            string fileName,
            int line)
        {
            return new SourcePausePointRawStackFrame(typeFullName, methodName, fileName, line);
        }
    }
}
