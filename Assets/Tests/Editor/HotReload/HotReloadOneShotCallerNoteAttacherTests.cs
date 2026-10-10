using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for attaching one-shot lifecycle notes to a run's method outcomes: which
    /// candidates are asked for a note and which outcomes a returned note replaces.
    /// </summary>
    public sealed class HotReloadOneShotCallerNoteAttacherTests
    {
        /// <summary>
        /// What: an outcome that already has a note (the transform worker's) is not asked for one
        /// and keeps its note.
        /// </summary>
        [Test]
        public void Attach_OutcomeThatAlreadyHasANote_IsNotRequestedAndKeepsItsNote()
        {
            const string workerNote = "Worker lifecycle note.";
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("Type.SetUp", "Assets/Test.cs", workerNote);
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteCandidate> candidates = new List<HotReloadOneShotCallerNoteCandidate>
            {
                CreateCandidate("Assembly.One", outcome)
            };
            List<HotReloadOneShotCallerNoteRequest> received = new List<HotReloadOneShotCallerNoteRequest>();

            HotReloadOneShotCallerNoteAttacher.Attach(
                outcomes,
                candidates,
                requests =>
                {
                    received.AddRange(requests);
                    return new string[requests.Count];
                });

            Assert.That(received, Is.Empty);
            Assert.That(outcomes[0].LifecycleNote, Is.EqualTo(workerNote));
        }

        /// <summary>
        /// What: each candidate becomes a request with its method and identity, and a returned note
        /// replaces that candidate's outcome while a null leaves its outcome untouched.
        /// </summary>
        [Test]
        public void Attach_NoteForARequest_ReplacesItsOutcome()
        {
            const string note = "One-shot note.";
            HotReloadMethodOutcome first = HotReloadMethodOutcome.Patched("Type.First", "Assets/First.cs");
            HotReloadMethodOutcome second = HotReloadMethodOutcome.Patched("Type.Second", "Assets/Second.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { first, second };
            List<HotReloadOneShotCallerNoteCandidate> candidates = new List<HotReloadOneShotCallerNoteCandidate>
            {
                CreateCandidate("Assembly.One", first),
                CreateCandidate("Assembly.Two", second)
            };
            List<HotReloadOneShotCallerNoteRequest> received = new List<HotReloadOneShotCallerNoteRequest>();

            HotReloadOneShotCallerNoteAttacher.Attach(
                outcomes,
                candidates,
                requests =>
                {
                    received.AddRange(requests);
                    return new[] { note, null };
                });

            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0].Method, Is.EqualTo("Type.First"));
            Assert.That(received[0].Identity.AssemblyName, Is.EqualTo("Assembly.One"));
            Assert.That(received[1].Method, Is.EqualTo("Type.Second"));
            Assert.That(received[1].Identity.AssemblyName, Is.EqualTo("Assembly.Two"));
            Assert.That(outcomes[0].LifecycleNote, Is.EqualTo(note));
            Assert.That(outcomes[1], Is.SameAs(second));
        }

        /// <summary>
        /// What: when two candidates point at the same outcome, the first note wins.
        /// </summary>
        [Test]
        public void Attach_TwoCandidatesForOneOutcome_KeepsTheFirstNote()
        {
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("Type.SetUp", "Assets/Test.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteCandidate> candidates = new List<HotReloadOneShotCallerNoteCandidate>
            {
                CreateCandidate("Assembly.One", outcome),
                CreateCandidate("Assembly.Two", outcome)
            };

            HotReloadOneShotCallerNoteAttacher.Attach(
                outcomes,
                candidates,
                requests => new[] { "first", "second" });

            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].LifecycleNote, Is.EqualTo("first"));
        }

        private static HotReloadOneShotCallerNoteCandidate CreateCandidate(
            string assemblyName,
            HotReloadMethodOutcome outcome)
        {
            HotReloadCompiledMethodIdentity identity =
                new HotReloadCompiledMethodIdentity(
                    assemblyName,
                    new HotReloadMetadataTypeName("Type"),
                    "SetUp",
                    Array.Empty<string>(),
                    0);
            return new HotReloadOneShotCallerNoteCandidate(identity, outcome);
        }
    }
}
