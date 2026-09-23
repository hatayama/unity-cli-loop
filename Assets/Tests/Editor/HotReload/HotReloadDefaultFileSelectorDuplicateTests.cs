using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how explicit --files entries that name the same script are reduced to one entry
    /// before the run, and how the response says so.
    /// </summary>
    public sealed class HotReloadDefaultFileSelectorDuplicateTests
    {
        private const string BoardPath = "Assets/Scripts/BoardView.cs";
        private const string HudPath = "Assets/Scripts/Hud.cs";
        private const string ScorePath = "Assets/Scripts/Score.cs";

        [SetUp]
        public void SetUp()
        {
            // The production tool captures the package roots before it normalizes paths; a direct
            // call to the real normalizer has to do the same.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
        }

        /// <summary>
        /// What: the same path listed twice is kept once, as the first raw entry, and the message
        /// names the path and the count.
        /// </summary>
        [Test]
        public void Resolve_WhenAPathIsListedTwice_KeepsTheFirstEntryAndNamesTheDuplicate()
        {
            HotReloadDefaultFileSelection selection = Resolve(BoardPath, BoardPath);

            Assert.That(selection.Files, Is.EqualTo(new[] { BoardPath }));
            Assert.That(
                selection.SelectionMessage,
                Is.EqualTo("--files listed '" + BoardPath + "' 2 times; it was processed once."));
        }

        /// <summary>
        /// What: a path listed three times is reported with the count three.
        /// </summary>
        [Test]
        public void Resolve_WhenAPathIsListedThreeTimes_ReportsThreeTimes()
        {
            HotReloadDefaultFileSelection selection = Resolve(BoardPath, HudPath, BoardPath, BoardPath);

            Assert.That(selection.Files, Is.EqualTo(new[] { BoardPath, HudPath }));
            Assert.That(
                selection.SelectionMessage,
                Is.EqualTo("--files listed '" + BoardPath + "' 3 times; it was processed once."));
        }

        /// <summary>
        /// What: when several paths repeat, one sentence names each in first-seen order.
        /// </summary>
        [Test]
        public void Resolve_WhenSeveralPathsRepeat_NamesThemInOneSentence()
        {
            HotReloadDefaultFileSelection selection = Resolve(
                HudPath,
                BoardPath,
                ScorePath,
                BoardPath,
                HudPath,
                BoardPath);

            Assert.That(selection.Files, Is.EqualTo(new[] { HudPath, BoardPath, ScorePath }));
            Assert.That(
                selection.SelectionMessage,
                Is.EqualTo(
                    "--files listed '" + HudPath + "' 2 times and '" + BoardPath + "' 3 times;"
                    + " each was processed once."));
        }

        /// <summary>
        /// What: a leading './' and a backslash separator still name the same script, so the
        /// spellings collapse to the first raw entry.
        /// </summary>
        [Test]
        public void Resolve_WhenSpellingsDifferInPrefixAndSeparator_TreatsThemAsOneFile()
        {
            string backslashPath = BoardPath.Replace('/', '\\');

            HotReloadDefaultFileSelection selection = Resolve("./" + BoardPath, backslashPath, BoardPath);

            Assert.That(selection.Files, Is.EqualTo(new[] { "./" + BoardPath }));
            Assert.That(
                selection.SelectionMessage,
                Is.EqualTo("--files listed '" + BoardPath + "' 3 times; it was processed once."));
        }

        /// <summary>
        /// What: paths that differ only in case are one file on Windows and two elsewhere, matching
        /// the file system comparison the rest of hot reload uses.
        /// </summary>
        [Test]
        public void Resolve_WhenPathsDifferOnlyInCase_FollowsThePlatformComparison()
        {
            string upperPath = BoardPath.ToUpperInvariant();

            HotReloadDefaultFileSelection selection = Resolve(BoardPath, upperPath);

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                Assert.That(selection.Files, Is.EqualTo(new[] { BoardPath }));
                Assert.That(selection.SelectionMessage, Does.StartWith("--files listed '"));
                return;
            }

            Assert.That(selection.Files, Is.EqualTo(new[] { BoardPath, upperPath }));
            Assert.That(selection.SelectionMessage, Is.Empty);
        }

        /// <summary>
        /// What: distinct paths pass through unchanged and in order, with no message.
        /// </summary>
        [Test]
        public void Resolve_WhenNoPathRepeats_KeepsTheInputAndAddsNoMessage()
        {
            HotReloadDefaultFileSelection selection = Resolve(ScorePath, BoardPath, HudPath);

            Assert.That(selection.Files, Is.EqualTo(new[] { ScorePath, BoardPath, HudPath }));
            Assert.That(selection.SelectionMessage, Is.Empty);
            Assert.That(selection.ValidationFailure, Is.Null);
        }

        private static HotReloadDefaultFileSelection Resolve(params string[] files)
        {
            return HotReloadDefaultFileSelector.Resolve(
                files,
                FailIfChangeDetectionRuns,
                Array.Empty<string>(),
                path => HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                    HotReloadCompositionRoot.Services.PackageRootCapture,
                    path));
        }

        private static HotReloadChangedFileAggregationResult FailIfChangeDetectionRuns()
        {
            throw new AssertionException("Explicit --files must not scan for changed files.");
        }
    }
}
