using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies hot-reload apply warns when a patched file's line count differs from last compiled source.
    /// </summary>
    public sealed class HotReloadUnpatchedMethodLineShiftWarningBuilderTests
    {
        /// <summary>
        /// What: a file whose edited source gained lines vs the last compiled snapshot warns that
        /// unpatched methods still resolve --line against compiled source.
        /// </summary>
        [Test]
        public void Build_WhenEditedLineCountDiffersFromCompiled_ReturnsShiftWarning()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "line1\nline2\nline3",
                "line1\nline2");

            Assert.That(
                warning,
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: a file whose edited source lost a line vs compiled still warns (count can shrink).
        /// </summary>
        [Test]
        public void Build_WhenEditedLineCountIsFewerThanCompiled_ReturnsShiftWarning()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "line1\nline2",
                "line1\nline2\nline3");

            Assert.That(
                warning,
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 2 lines vs compiled 3). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: a same-line-count edit (body-only change) does not emit the line-shift warning.
        /// </summary>
        [Test]
        public void Build_WhenEditedAndCompiledLineCountsMatch_ReturnsEmpty()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "alpha\nbeta",
                "alpha-edited\nbeta-edited");

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: CRLF vs LF with the same logical lines is not a count change after newline normalization.
        /// </summary>
        [Test]
        public void Build_WhenCrlfAndLfHaveSameLogicalLineCount_ReturnsEmpty()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "line1\r\nline2",
                "line1\nline2");

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a trailing newline alone increases the split count and therefore warns.
        /// </summary>
        [Test]
        public void Build_WhenOnlyTrailingNewlineAddsALine_ReturnsShiftWarning()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "line1\nline2\n",
                "line1\nline2");

            Assert.That(
                warning,
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: a missing compiled snapshot cannot claim a line-count shift, so no warning is emitted.
        /// </summary>
        [Test]
        public void Build_WhenCompiledSourceIsMissing_ReturnsEmpty()
        {
            string warning = HotReloadUnpatchedMethodLineShiftWarningBuilder.Build(
                "Assets/Scripts/Player.cs",
                "line1\nline2\nline3",
                null);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: two methods in the same shifted file produce a single warning for that file.
        /// </summary>
        [Test]
        public void Append_WhenTwoMethodsShareAShiftedFile_AddsOneWarning()
        {
            List<string> warnings = new List<string>();
            List<HotReloadMethodOutcome> methods = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Player.Jump", "Assets/Scripts/Player.cs"),
                HotReloadMethodOutcome.Skipped("Player.Move", "unchanged body", "Assets/Scripts/Player.cs")
            };

            HotReloadUnpatchedMethodLineShiftWarningBuilder.Append(
                warnings,
                methods,
                file => "line1\nline2\nline3",
                file => "line1\nline2");

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: relative and absolute spellings of the same apply file emit one warning labeled
        /// with the project-relative path.
        /// </summary>
        [Test]
        public void Append_WhenSameFileIsRelativeAndAbsolute_AddsOneWarningLabeledWithRelativePath()
        {
            string relativePath = "Assets/Scripts/Player.cs";
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            List<string> warnings = new List<string>();
            List<HotReloadMethodOutcome> methods = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Player.Jump", relativePath),
                HotReloadMethodOutcome.Patched("Player.Move", absolutePath)
            };

            HotReloadUnpatchedMethodLineShiftWarningBuilder.Append(
                warnings,
                methods,
                file => "line1\nline2\nline3",
                file => "line1\nline2");

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: a two-file apply warns only for the file whose line count changed.
        /// </summary>
        [Test]
        public void Append_WhenOneFileShiftedAndOneUnchanged_AddsOnlyShiftedFileWarning()
        {
            List<string> warnings = new List<string>();
            List<HotReloadMethodOutcome> methods = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Player.Jump", "Assets/Scripts/Player.cs"),
                HotReloadMethodOutcome.Patched("Enemy.Idle", "Assets/Scripts/Enemy.cs")
            };

            HotReloadUnpatchedMethodLineShiftWarningBuilder.Append(
                warnings,
                methods,
                file => file.IndexOf("Player", StringComparison.Ordinal) >= 0
                    ? "line1\nline2\nline3"
                    : "same\ncount",
                file => file.IndexOf("Player", StringComparison.Ordinal) >= 0
                    ? "line1\nline2"
                    : "same\ncount");

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: two files that both change line count each get their own warning.
        /// </summary>
        [Test]
        public void Append_WhenTwoFilesBothShifted_AddsTwoWarnings()
        {
            List<string> warnings = new List<string>();
            List<HotReloadMethodOutcome> methods = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Patched("Player.Jump", "Assets/Scripts/Player.cs"),
                HotReloadMethodOutcome.Patched("Enemy.Idle", "Assets/Scripts/Enemy.cs")
            };

            HotReloadUnpatchedMethodLineShiftWarningBuilder.Append(
                warnings,
                methods,
                file => file.IndexOf("Player", StringComparison.Ordinal) >= 0
                    ? "line1\nline2\nline3"
                    : "a\nb\nc\nd",
                file => file.IndexOf("Player", StringComparison.Ordinal) >= 0
                    ? "line1\nline2"
                    : "a\nb");

            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(
                warnings[0],
                Is.EqualTo(
                    "Assets/Scripts/Player.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
            Assert.That(
                warnings[1],
                Is.EqualTo(
                    "Assets/Scripts/Enemy.cs: line count differs from the last compiled source (edited 4 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
        }

        /// <summary>
        /// What: a line-count warning counted with one other hot-reload warning keeps the
        /// single-compile resolution suffix, because compile restores compiled-source line numbers.
        /// </summary>
        [Test]
        public void BuildApplyResponse_WhenLineShiftAndOneOtherHotReloadWarning_AppendsSingleCompileResolution()
        {
            string relativePath = "Library/UloopHotReload/TestSources/line-shift-warning-probe.cs";
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, "line1\nline2\nline3");

            Func<string, string> previousLoader =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => "line1\nline2";
            try
            {
                HotReloadResponse response = HotReloadTool.BuildApplyResponse(
                    new HotReloadOrchestratorResult(
                        new List<HotReloadMethodOutcome>
                        {
                            HotReloadMethodOutcome.Patched("Probe.M", relativePath)
                        },
                        new List<string> { "const-drift" },
                        patchedTotal: 1,
                        activePatchTotal: 1));

                Assert.That(response.Warnings.Count, Is.EqualTo(2));
                Assert.That(response.Warnings[0], Is.EqualTo("const-drift"));
                Assert.That(
                    response.Warnings[1],
                    Is.EqualTo(
                        "Library/UloopHotReload/TestSources/line-shift-warning-probe.cs: line count differs from the last compiled source (edited 3 lines vs compiled 2). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line."));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. "
                        + "2 warning(s). See Warnings. "
                        + "A single 'uloop compile' clears all of them at once."));
            }
            finally
            {
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previousLoader;
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }
            }
        }
    }
}
