using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers enabling a pause point on a script named in each path form: an embedded package's
    /// asset path, the path under its folder relative to the project, the absolute path, and an
    /// absolute Assets path. Every form arms the marker named by the asset path, and the edited
    /// file and the resolved line text are read from the file on disk.
    /// </summary>
    public sealed class PausePointScriptPathFormTests
    {
        private const string FixtureAssetPath =
            "Packages/io.github.hatayama.uloop.hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const string FixturePhysicalPath =
            "Packages/uloop-hotreload-package-fixture/Runtime/HotReloadPackageFixture.cs";
        private const int SecondStatementLine = 16;
        private const string SecondStatementText = "return 2;";
        private const string AssetsFixturePath =
            "Assets/Tests/Editor/HotReload/HotReloadPausePointLineDriftFixture.cs";
        private const string AssetsFixtureStatementText = "return 22;";
        // Part of the warning added when --line could not be mapped through a verified snapshot.
        private const string NoVerifiedSnapshotWarningPart = "No verified source snapshot";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: a package script named by its asset path arms the requested statement on the
        /// edited-file basis, through the real snapshot and the file on disk.
        /// </summary>
        [Test]
        public void Enable_PackageSourceGivenByItsAssetPath_ArmsTheStatementOnTheEditedFileBasis()
        {
            HotReloadSourceSnapshotter.CaptureAfterDomainReload();

            PausePointResponse response = Enable(FixtureAssetPath, SecondStatementLine);

            AssertArmedSecondOnTheEditedFileBasis(response);
        }

        /// <summary>
        /// What: the same script named by the path under the package's folder arms the marker named
        /// by the asset path.
        /// </summary>
        [Test]
        public void Enable_PackageSourceGivenByItsPhysicalPath_ArmsTheSameMarkerAsTheAssetPath()
        {
            HotReloadSourceSnapshotter.CaptureAfterDomainReload();

            PausePointResponse response = Enable(FixturePhysicalPath, SecondStatementLine);

            AssertArmedSecondOnTheEditedFileBasis(response);
        }

        /// <summary>
        /// What: the same script named by its absolute path arms the marker named by the asset path.
        /// </summary>
        [Test]
        public void Enable_PackageSourceGivenByItsAbsolutePhysicalPath_ArmsTheSameMarkerAsTheAssetPath()
        {
            HotReloadSourceSnapshotter.CaptureAfterDomainReload();

            PausePointResponse response = Enable(ToAbsolutePath(FixturePhysicalPath), SecondStatementLine);

            AssertArmedSecondOnTheEditedFileBasis(response);
        }

        /// <summary>
        /// What: when the compiled source had the requested statement one line lower, --line is
        /// mapped through the snapshot, and the mapped compiled line is found in the PDB, which
        /// records a package script by the path under its folder rather than by its asset path.
        /// In this harness the PDB line numbers match the file on disk, so the injected snapshot
        /// plays the compiled source (the roles are reversed from production): edited line 16 maps
        /// to compiled line 17, which the PDB places on Second's closing brace, and that maps back
        /// to edited line 16.
        /// </summary>
        [Test]
        public void Enable_PackageSourceWithASnapshotShiftedBelowTheRequestedLine_MapsTheLineThroughTheSnapshot()
        {
            string shifted = InsertBlankLinesAfterLine(ReadFixtureFromDisk(), 8, 1);

            PausePointResponse response = EnableWithSnapshot(FixtureAssetPath, SecondStatementLine, shifted);

            AssertArmedSecondOnTheEditedFileBasis(response);
        }

        /// <summary>
        /// What: the same mapping holds when the script is named by the path under the package's
        /// folder, and the snapshot is asked for by the asset path it is keyed by. The roles of
        /// the snapshot and the PDB are reversed from production, as in the test above.
        /// </summary>
        [Test]
        public void Enable_PackageSourceGivenByItsPhysicalPathWithAShiftedSnapshot_MapsTheLineThroughTheSnapshot()
        {
            string shifted = InsertBlankLinesAfterLine(ReadFixtureFromDisk(), 8, 1);

            PausePointResponse response = EnableWithSnapshot(FixturePhysicalPath, SecondStatementLine, shifted);

            AssertArmedSecondOnTheEditedFileBasis(response);
        }

        /// <summary>
        /// What: an Assets script named by its absolute path arms the marker named by its
        /// project-relative path, on the edited-file basis.
        /// </summary>
        [Test]
        public void Enable_AssetsSourceGivenByItsAbsolutePath_ArmsTheSameMarkerAsTheRelativePath()
        {
            HotReloadSourceSnapshotter.CaptureAfterDomainReload();
            int line = FindLineNumber(File.ReadAllText(ToAbsolutePath(AssetsFixturePath)), AssetsFixtureStatementText);
            Assert.That(line, Is.GreaterThan(0));

            PausePointResponse response = Enable(ToAbsolutePath(AssetsFixturePath), line);

            Assert.That(response.Success, Is.True, response.Message + " / " + response.RecommendedNextAction);
            Assert.That(response.Id, Is.EqualTo(AssetsFixturePath + ":" + line));
            Assert.That(response.LineBasis, Is.EqualTo("EditedFile"));
            Assert.That(response.ResolvedLineText, Does.Contain(AssetsFixtureStatementText));
        }

        private static PausePointResponse Enable(string file, int line)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = file,
                Line = line,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private static PausePointResponse EnableWithSnapshot(string file, int line, string snapshot)
        {
            using (HotReloadSidePortScope scope = new HotReloadSidePortScope())
            {
                scope.Port.VerifiedSnapshotSource = (string snapshotFile, string dllPath) =>
                {
                    // Snapshots are keyed by the asset path, so any other form here would miss it.
                    Assert.That(snapshotFile, Is.EqualTo(FixtureAssetPath));
                    return snapshot;
                };
                return Enable(file, line);
            }
        }

        private static void AssertArmedSecondOnTheEditedFileBasis(PausePointResponse response)
        {
            Assert.That(response.Success, Is.True, response.Message + " / " + response.RecommendedNextAction);
            Assert.That(response.Id, Is.EqualTo(FixtureAssetPath + ":" + SecondStatementLine));
            Assert.That(response.ResolvedMethod, Does.Contain("Second"));
            Assert.That(response.LineBasis, Is.EqualTo("EditedFile"));
            Assert.That(response.ResolvedLine, Is.EqualTo(SecondStatementLine));
            Assert.That(response.ResolvedLineText, Does.Contain(SecondStatementText));
            Assert.That(response.Warning ?? string.Empty, Does.Not.Contain(NoVerifiedSnapshotWarningPart));
        }

        private static string ReadFixtureFromDisk()
        {
            return File.ReadAllText(ToAbsolutePath(FixturePhysicalPath));
        }

        private static string ToAbsolutePath(string projectRelativePath)
        {
            return Path.GetFullPath(Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), projectRelativePath));
        }

        private static string InsertBlankLinesAfterLine(string source, int line, int count)
        {
            List<string> lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
            lines.InsertRange(line, Enumerable.Repeat(string.Empty, count));
            return string.Join("\n", lines);
        }

        private static int FindLineNumber(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }
    }
}
