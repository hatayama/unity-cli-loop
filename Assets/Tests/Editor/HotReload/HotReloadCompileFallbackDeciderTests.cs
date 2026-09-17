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
        /// <summary>
        /// What: the decision for each option, with and without an unapplied edit, in and out of
        /// Play Mode. Expected values are the names the response carries, because the decision
        /// enum is internal and reaches the wire through its name.
        /// </summary>
        [TestCase(HotReloadCompileOnSkip.auto, true, false, "Requested")]
        [TestCase(HotReloadCompileOnSkip.auto, true, true, "HeldForPlayMode")]
        [TestCase(HotReloadCompileOnSkip.auto, false, false, "NotNeeded")]
        [TestCase(HotReloadCompileOnSkip.auto, false, true, "NotNeeded")]
        [TestCase(HotReloadCompileOnSkip.on, true, true, "Requested")]
        [TestCase(HotReloadCompileOnSkip.off, true, false, "Disabled")]
        [TestCase(HotReloadCompileOnSkip.off, false, true, "NotNeeded")]
        public void Decide_OptionAndRunState_ChoosesTheFallback(
            HotReloadCompileOnSkip option,
            bool hasUnappliedEdit,
            bool isPlaying,
            string expected)
        {
            Assert.That(
                HotReloadCompileFallbackDecider.Decide(option, hasUnappliedEdit, isPlaying).ToString(),
                Is.EqualTo(expected));
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

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result), Is.True);
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

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result), Is.True);
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

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result), Is.True);
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

            Assert.That(HotReloadCompileFallbackDecider.HasUnappliedEdit(result), Is.False);
        }
    }
}
