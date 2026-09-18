using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the per-kind summary of re-applied siblings' missing baselines.
    /// </summary>
    public class HotReloadSiblingBaselineNoticesTests
    {
        private const string FirstPath = "Assets/Scripts/Alpha.cs";
        private const string SecondPath = "Assets/Scripts/Beta.cs";

        /// <summary>
        /// What: siblings of one kind become a single warning whose count matches the sorted
        /// paths it lists, and a path reported twice is counted once.
        /// </summary>
        [Test]
        public void AppendTo_SiblingsOfOneKind_BecomeOneWarningCountingEachPathOnce()
        {
            HotReloadSiblingBaselineNotices notices = new HotReloadSiblingBaselineNotices();
            notices.Add(HotReloadMissingBaselineKind.IntroducedType, SecondPath);
            notices.Add(HotReloadMissingBaselineKind.IntroducedType, FirstPath);
            notices.Add(HotReloadMissingBaselineKind.IntroducedType, SecondPath);
            List<string> warnings = new List<string>();

            notices.AppendTo(warnings);

            Assert.That(
                warnings,
                Is.EqualTo(new[]
                {
                    "2 re-applied sibling file(s) declare a type hot reload introduced, so they have "
                    + "no compiled baseline until 'uloop compile': " + FirstPath + ", " + SecondPath
                    + ". This is expected: hot reload tracks each introduced type from its own "
                    + "recorded declaration."
                }));
        }

        /// <summary>
        /// What: each kind keeps its own warning, in a fixed order whatever order the siblings
        /// were reported in.
        /// </summary>
        [Test]
        public void AppendTo_SiblingsOfDifferentKinds_KeepOneWarningPerKindInAFixedOrder()
        {
            HotReloadSiblingBaselineNotices notices = new HotReloadSiblingBaselineNotices();
            notices.Add(HotReloadMissingBaselineKind.NoCompiledMethodBody, FirstPath);
            notices.Add(HotReloadMissingBaselineKind.NoVerifiedSourceSnapshot, SecondPath);
            List<string> warnings = new List<string>();

            notices.AppendTo(warnings);

            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(
                warnings[0],
                Does.StartWith("1 re-applied sibling file(s) have no verified source snapshot"));
            Assert.That(warnings[0], Does.Contain(SecondPath));
            Assert.That(
                warnings[1],
                Does.StartWith("1 re-applied sibling file(s) have no compiled method body"));
            Assert.That(warnings[1], Does.Contain(FirstPath));
        }

        /// <summary>
        /// What: a run with no re-applied sibling missing a baseline adds no warning.
        /// </summary>
        [Test]
        public void AppendTo_WithNoSiblings_AddsNoWarning()
        {
            HotReloadSiblingBaselineNotices notices = new HotReloadSiblingBaselineNotices();
            List<string> warnings = new List<string>();

            notices.AppendTo(warnings);

            Assert.That(warnings, Is.Empty);
        }
    }
}
