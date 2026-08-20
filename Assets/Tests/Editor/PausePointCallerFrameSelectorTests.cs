using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        /// What: a Harmony-patched caller (DMD declaring type and _PatchN name) is reported
        /// as a method-only frame under its original Type.Method name.
        /// </summary>
        [Test]
        public void Select_WhenCallerIsHarmonyPatchedBody_ReportsOriginalMethodNameWithoutFileOrLine()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "Game.Player.Jump_Patch1",
                    null,
                    0),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "Game.Input.HandleJump_Patch1",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a multi-digit Harmony patch suffix still resolves to the original method name.
        /// </summary>
        [Test]
        public void Select_WhenHarmonyPatchSuffixIsMultiDigit_ReportsOriginalMethodName()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "Game.Player.Jump_Patch1",
                    null,
                    0),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "Game.Input.HandleJump_Patch12",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a DMD frame whose name has no Harmony _Patch digits suffix stays skipped as
        /// genuine MonoMod infrastructure.
        /// </summary>
        [Test]
        public void Select_WhenDmdNameHasNoPatchSuffix_OmitsThatFrame()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "MonoMod.Utils.DynamicMethodDefinition.Generate",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        /// <summary>
        /// What: a DMD frame with a non-digit tail after _Patch stays skipped.
        /// </summary>
        [Test]
        public void Select_WhenDmdNameHasNonDigitPatchTail_OmitsThatFrame()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "Foo.Bar_PatchX",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        /// <summary>
        /// What: a Harmony-patched uloop-internal caller stays skipped after logical-name
        /// resolution, matching the compiled-counterpart skip policy.
        /// </summary>
        [Test]
        public void Select_WhenPatchedCallerIsUloopInternal_OmitsThatFrame()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "io.github.hatayama.UnityCliLoop.Foo.Bar_Patch1",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Is.Empty);
        }

        /// <summary>
        /// What: Dump 3 shape (marker DMD + patched caller DMD + patched Update DMD) yields
        /// both callers as method-only frames instead of an empty payload.
        /// </summary>
        [Test]
        public void Select_WhenMarkerAndCallersAreAllHarmonyPatched_ReportsBothCallers()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "CallerFrameProbe.ShallowMarker_Patch3",
                    null,
                    0),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "CallerFrameProbe.ShallowCaller_Patch3",
                    null,
                    0),
                CreateRawFrame(
                    SourcePausePointConstants.HarmonyDynamicMethodDeclaringType,
                    "CallerFrameProbe.Update_Patch1",
                    null,
                    0),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(selected[0].Method, Is.EqualTo("CallerFrameProbe.ShallowCaller"));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
            Assert.That(selected[1].Method, Is.EqualTo("CallerFrameProbe.Update"));
            Assert.That(selected[1].File, Is.Null);
            Assert.That(selected[1].Line, Is.EqualTo(0));
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
        /// What: an absolute macOS path under Assets/ is stripped to a project-relative path.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenAbsoluteAssetsPath_ReturnsProjectRelative()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "/Users/<USER_NAME>/project/Assets/Scripts/Input.cs");

            Assert.That(normalized, Is.EqualTo("Assets/Scripts/Input.cs"));
        }

        /// <summary>
        /// What: an absolute path under Packages/ is stripped to a project-relative path.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenAbsolutePackagesPath_ReturnsProjectRelative()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "C:/Users/<USER_NAME>/project/Packages/src/Foo.cs");

            Assert.That(normalized, Is.EqualTo("Packages/src/Foo.cs"));
        }

        /// <summary>
        /// What: an absolute path under Library/PackageCache/ is stripped to a project-relative path.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenAbsolutePackageCachePath_ReturnsProjectRelative()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "/Users/<USER_NAME>/project/Library/PackageCache/com.example.pkg@1.2.3/Runtime/Foo.cs");

            Assert.That(normalized, Is.EqualTo("Library/PackageCache/com.example.pkg@1.2.3/Runtime/Foo.cs"));
        }

        /// <summary>
        /// What: a rooted path with no recognizable project segment normalizes to null instead of
        /// leaking a machine path into the payload.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenRootedPathHasNoProjectSegment_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "/Users/<USER_NAME>/External/Src/Foo.cs");

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a Windows drive path with no recognizable project segment normalizes to null.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenDrivePathHasNoProjectSegment_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "C:/External/Src/Foo.cs");

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a relative path outside the known Unity roots normalizes to null instead of
        /// leaking non-project structure into the payload.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenRelativePathOutsideKnownRoots_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "External/Src/Foo.cs");

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a parent-directory escape with no known project segment normalizes to null.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenParentDirectoryEscape_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "../External/Src/Foo.cs");

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a parent-directory escape in front of a known root normalizes to null
        /// instead of masquerading as a project path.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenParentDirectoryPrecedesKnownRoot_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "../Assets/Scripts/Foo.cs");

            Assert.That(normalized, Is.Null);
        }

        /// <summary>
        /// What: a parent-directory segment after a known root normalizes to null instead
        /// of leaking non-project structure through the whitelist.
        /// </summary>
        [Test]
        public void NormalizeFilePath_WhenParentDirectorySegmentInsideKnownRoot_ReturnsNull()
        {
            string normalized = SourcePausePointCallerFrameSelector.NormalizeFilePath(
                "Assets/../External/Src/Foo.cs");

            Assert.That(normalized, Is.Null);
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

        /// <summary>
        /// What: Select demangles async state-machine callers, so deleting FormatMethodDisplay
        /// from Select fails this case even if the helper still has its own tests.
        /// </summary>
        [Test]
        public void Select_WhenCallerIsAsyncStateMachine_ReportsLogicalMethodName()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame("Game.Enemy+<Chase>d__7", "MoveNext", "Assets/Scripts/Enemy.cs", 55),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].Method, Is.EqualTo("Game.Enemy.Chase"));
            Assert.That(selected[0].File, Is.EqualTo("Assets/Scripts/Enemy.cs"));
            Assert.That(selected[0].Line, Is.EqualTo(55));
        }

        /// <summary>
        /// What: Select normalizes Windows separators and a leading ./, so deleting
        /// NormalizeFilePath from Select fails this case even if the helper still has
        /// its own tests.
        /// </summary>
        [Test]
        public void Select_WhenFileHasDotSlashAndBackslashes_NormalizesThroughSelect()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(UserType, UserMethod, ".\\Assets\\Scripts\\Input.cs", 10),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].File, Is.EqualTo("Assets/Scripts/Input.cs"));
            Assert.That(selected[0].Line, Is.EqualTo(10));
        }

        /// <summary>
        /// What: Select strips an absolute Assets/ path to project-relative form, so deleting
        /// that step from Select fails this case even if the helper still has its own tests.
        /// </summary>
        [Test]
        public void Select_WhenFileIsAbsoluteAssetsPath_NormalizesThroughSelect()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(
                    UserType,
                    UserMethod,
                    "/Users/<USER_NAME>/project/Assets/Scripts/Input.cs",
                    10),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].File, Is.EqualTo("Assets/Scripts/Input.cs"));
            Assert.That(selected[0].Line, Is.EqualTo(10));
        }

        /// <summary>
        /// What: Select degrades a rooted no-project-segment path to a method-only frame with
        /// File null and Line 0, so the payload never carries an absolute machine path.
        /// </summary>
        [Test]
        public void Select_WhenFileIsRootedWithoutProjectSegment_DegradesToMethodOnlyFrame()
        {
            SourcePausePointRawStackFrame[] rawFrames =
            {
                CreateRawFrame(MarkerType, MarkerMethod, MarkerFile, MarkerLine),
                CreateRawFrame(UserType, UserMethod, "/Users/<USER_NAME>/External/Src/Foo.cs", 10),
            };

            List<UloopPausePointCallerFrame> selected = SourcePausePointCallerFrameSelector.Select(rawFrames);

            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0].File, Is.Null);
            Assert.That(selected[0].Line, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a live StackTrace walk treats Level2 as the marker, skips capture
        /// infrastructure, and returns Level1 then Level0 as the two nearest callers.
        /// </summary>
        [Test]
        public void CaptureCallerFrames_OnRealStack_ReturnsTwoNearestCallersAboveMarker()
        {
            List<UloopPausePointCallerFrame> selected =
                PausePointCallerFrameLiveTestSupport.StackHost.Level0();

            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(
                selected[0].Method,
                Is.EqualTo("PausePointCallerFrameLiveTestSupport.StackHost.Level1"));
            Assert.That(
                selected[1].Method,
                Is.EqualTo("PausePointCallerFrameLiveTestSupport.StackHost.Level0"));
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

// Why a non-uloop namespace: the selector skips types that start with
// io.github.hatayama.UnityCliLoop, so a helper in the test namespace would
// be dropped as infrastructure and a stubbed CaptureCallerFrames would still
// leave every other test green.
namespace PausePointCallerFrameLiveTestSupport
{
    internal static class StackHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<UloopPausePointCallerFrame> Level0() { return Level1(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<UloopPausePointCallerFrame> Level1() { return Level2(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static List<UloopPausePointCallerFrame> Level2() { return SourcePausePointCallerFrameCapture.CaptureCallerFrames(); }
    }
}
