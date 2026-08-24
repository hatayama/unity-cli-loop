using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for caller-aware one-shot lifecycle notes.
    /// </summary>
    public sealed class HotReloadOneShotCallerNoteBuilderTests
    {
        /// <summary>
        /// What: one Awake caller produces the caller-aware lifecycle note.
        /// </summary>
        [Test]
        public void Build_OneAwakeCaller_ReturnsIndirectLifecycleNote()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Awake", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(
                note,
                Is.EqualTo(
                    "SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."));
        }

        /// <summary>
        /// What: duplicate lifecycle callers are de-duplicated and ordered ordinally in the note.
        /// </summary>
        [Test]
        public void Build_DuplicateAwakeAndStartCallers_ReturnsSortedDistinctLifecycleNames()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Start", true),
                    new OneShotCallerClassification("Awake", true),
                    new OneShotCallerClassification("Awake", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(
                note,
                Is.EqualTo(
                    "SetUp is called only from one-shot lifecycle method(s) (Awake, Start) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."));
        }

        /// <summary>
        /// What: a non-lifecycle caller suppresses the note so the only-callers claim is not guessed.
        /// </summary>
        [Test]
        public void Build_MixedCallerClassifications_ReturnsNull()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Awake", true),
                    new OneShotCallerClassification("Update", false)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(note, Is.Null);
        }

        /// <summary>
        /// What: no compiled callers suppresses the note because non-IL reachability is unknown.
        /// </summary>
        [Test]
        public void Build_NoCallers_ReturnsNull()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                Array.Empty<OneShotCallerClassification>();

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(note, Is.Null);
        }
    }
}
