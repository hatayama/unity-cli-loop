using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the pure decision of whether a run that left edits unapplied asks the CLI for a
    /// compile, and what counts as an unapplied edit.
    /// </summary>
    public class HotReloadCompileFallbackDeciderTests
    {
        private const string RequestedPath = "Assets/Requested.cs";
        private const string SiblingPath = "Assets/Sibling.cs";

        /// <summary>
        /// What: the decision for each option, with and without an unapplied edit, in and out of
        /// Play Mode, and with the Editor allowing or refusing compiles during play. Expected
        /// values are the names the response carries, because the decision enum is internal and
        /// reaches the wire through its name.
        /// </summary>
        [TestCase(HotReloadCompileOnSkip.auto, true, false, false, "Requested")]
        [TestCase(HotReloadCompileOnSkip.auto, true, true, false, "HeldForPlayMode")]
        [TestCase(HotReloadCompileOnSkip.auto, true, true, true, "HeldForPlayMode")]
        [TestCase(HotReloadCompileOnSkip.auto, false, false, false, "NotNeeded")]
        [TestCase(HotReloadCompileOnSkip.auto, false, true, false, "NotNeeded")]
        [TestCase(HotReloadCompileOnSkip.on, true, true, false, "Requested")]
        [TestCase(HotReloadCompileOnSkip.on, true, false, true, "Requested")]
        [TestCase(HotReloadCompileOnSkip.on, false, true, true, "NotNeeded")]
        [TestCase(HotReloadCompileOnSkip.off, true, false, false, "Disabled")]
        [TestCase(HotReloadCompileOnSkip.off, true, true, true, "Disabled")]
        [TestCase(HotReloadCompileOnSkip.off, false, true, false, "NotNeeded")]
        public void Decide_OptionAndRunState_ChoosesTheFallback(
            HotReloadCompileOnSkip option,
            bool hasUnappliedEdit,
            bool isPlaying,
            bool compileRefusedDuringPlay,
            string expected)
        {
            Assert.That(
                HotReloadCompileFallbackDecider.Decide(
                    option,
                    hasUnappliedEdit,
                    isPlaying,
                    compileRefusedDuringPlay).ToString(),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// What: --compile-on-skip on during play does not request a compile the Editor is set to
        /// refuse, so the CLI does not run one that can only fail.
        /// </summary>
        [Test]
        public void Decide_OnDuringPlayWhenTheEditorRefusesCompiles_ReturnsBlockedByPlayModeSetting()
        {
            Assert.That(
                HotReloadCompileFallbackDecider.Decide(
                    HotReloadCompileOnSkip.on,
                    hasUnappliedEdit: true,
                    isPlaying: true,
                    compileRefusedDuringPlay: true).ToString(),
                Is.EqualTo("BlockedByPlayModeSetting"));
        }

        /// <summary>
        /// What: --compile-on-skip on during play still requests the compile when the Editor
        /// compiles during play.
        /// </summary>
        [Test]
        public void Decide_OnDuringPlayWhenTheEditorAllowsCompiles_ReturnsRequested()
        {
            Assert.That(
                HotReloadCompileFallbackDecider.Decide(
                    HotReloadCompileOnSkip.on,
                    hasUnappliedEdit: true,
                    isPlaying: true,
                    compileRefusedDuringPlay: false).ToString(),
                Is.EqualTo("Requested"));
        }

        /// <summary>
        /// What: one skipped method makes the run count as leaving an edit unapplied.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_SkippedMethod_IsTrue()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("A.Foo()", "reason", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result, NoActivePatchSiblings()), Is.True);
        }

        /// <summary>
        /// What: one failed method makes the run count as leaving an edit unapplied.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_FailedMethod_IsTrue()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Failed("A.Foo()", "reason", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 0,
                activePatchTotal: 0);

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result, NoActivePatchSiblings()), Is.True);
        }

        /// <summary>
        /// What: a refused type declaration counts even when every method outcome applied.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_FailedIntroducedTypeBesidePatchedMethods_IsTrue()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("A.Foo()", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                introducedTypes: new[]
                {
                    HotReloadIntroducedTypeOutcome.Failed(
                        "Example.Introduced",
                        "ExampleAssembly",
                        "Assets/A.cs",
                        "reason")
                });

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result, NoActivePatchSiblings()), Is.True);
        }

        /// <summary>
        /// What: a run whose every method and type outcome applied leaves nothing for a compile.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_EveryOutcomeApplied_IsFalse()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("A.Foo()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Added("A.Bar()", "Assets/A.cs"),
                    HotReloadMethodOutcome.AlreadyActive("A.Baz()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Stale("A.Gone()", "Assets/A.cs")
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                introducedTypes: new[]
                {
                    HotReloadIntroducedTypeOutcome.Introduced(
                        "Example.Introduced",
                        "ExampleAssembly",
                        "Assets/A.cs"),
                    HotReloadIntroducedTypeOutcome.AlreadyActive(
                        "Example.Retained",
                        "ExampleAssembly",
                        "Assets/A.cs",
                        bodyEdited: false)
                });

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result, NoActivePatchSiblings()), Is.False);
        }

        /// <summary>
        /// What: a Skipped row of a sibling the run pulled in to re-apply its earlier changes does
        /// not count, because it is not an edit this run was asked to apply and its earlier patch
        /// stays active.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_OnlyASiblingRowIsSkipped_ReturnsFalse()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Requested.M()", RequestedPath),
                    HotReloadMethodOutcome.Skipped("Sibling.N()", "reason", SiblingPath)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                reappliedSiblingPaths: new[] { SiblingPath });

            Assert.That(
                HotReloadCompileFallbackDecider.HasUnappliedEdit(result, ActivePatchSiblings(SiblingPath)),
                Is.False);
        }

        /// <summary>
        /// What: a Skipped row of a file the run was passed still counts when a sibling row beside
        /// it applied.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_RequestedRowIsSkippedBesideASiblingRow_ReturnsTrue()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Skipped("Requested.M()", "reason", RequestedPath),
                    HotReloadMethodOutcome.Patched("Sibling.N()", SiblingPath)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                reappliedSiblingPaths: new[] { SiblingPath });

            Assert.That(
                HotReloadCompileFallbackDecider.HasUnappliedEdit(result, ActivePatchSiblings(SiblingPath)),
                Is.True);
        }

        /// <summary>
        /// What: a refused type declaration owned by a sibling pulled in for its earlier changes
        /// does not count.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_OnlyASiblingIntroducedTypeFailed_ReturnsFalse()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Requested.M()", RequestedPath)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                reappliedSiblingPaths: new[] { SiblingPath },
                introducedTypes: new[]
                {
                    HotReloadIntroducedTypeOutcome.Failed(
                        "Example.Introduced",
                        "ExampleAssembly",
                        SiblingPath,
                        "reason")
                });

            Assert.That(
                HotReloadCompileFallbackDecider.HasUnappliedEdit(result, ActivePatchSiblings(SiblingPath)),
                Is.False);
        }

        /// <summary>
        /// What: a Skipped row of a sibling that came back for another reason than its active
        /// changes (a retry after an earlier Skip, or a companion) still counts, because that row
        /// is an edit that was never applied.
        /// </summary>
        [Test]
        public void HasUnappliedEdit_RetriedSiblingRowIsSkipped_ReturnsTrue()
        {
            HotReloadOrchestratorResult result = new HotReloadOrchestratorResult(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Requested.M()", RequestedPath),
                    HotReloadMethodOutcome.Skipped("Sibling.N()", "reason", SiblingPath)
                },
                new List<string>(),
                patchedTotal: 1,
                activePatchTotal: 1,
                reappliedSiblingPaths: new[] { SiblingPath });

            Assert.That(
                HotReloadCompileFallbackDecider.HasUnappliedEdit(result, NoActivePatchSiblings()),
                Is.True);
        }

        private static HotReloadReappliedSiblingFiles NoActivePatchSiblings()
        {
            return new HotReloadReappliedSiblingFiles(Array.Empty<string>(), path => path);
        }

        private static HotReloadReappliedSiblingFiles ActivePatchSiblings(string siblingPath)
        {
            return new HotReloadReappliedSiblingFiles(new[] { siblingPath }, path => path);
        }
    }
}
