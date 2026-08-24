using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEngine;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for caller-aware one-shot lifecycle notes.
    /// </summary>
    public sealed class HotReloadOneShotCallerNoteBuilderTests
    {
        /// <summary>
        /// What: a caller whose method name is not a Unity one-shot message is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_NonLifecycleName_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ValidLifecycleFixture), "Update"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a parameterless void Awake declared on a MonoBehaviour is classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_ValidAwake_ReturnsTrue()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ValidLifecycleFixture), "Awake"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// What: an unresolvable caller type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_UnresolvableType_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit("Missing.Namespace.Type", "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a non-MonoBehaviour caller type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_NonMonoBehaviour_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(NonMonoBehaviourFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a lifecycle-named method with parameters is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_MethodWithParameter_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ParameterLifecycleFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a lifecycle-named method with a non-void return type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_MethodWithReturnValue_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ReturnValueLifecycleFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: an incomplete scan does not add caller-aware notes to its candidates.
        /// </summary>
        [Test]
        public void ApplyNotes_MissingScanAssembly_SuppressesNotes()
        {
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("Type.SetUp", "Assets/Test.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateCandidate("Assembly.One", outcome)
                };

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                "project",
                outcomes,
                candidates,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteScanner.CallSiteHit>(),
                    new List<string> { assemblyName }));

            Assert.That(outcomes[0].LifecycleNote, Is.Empty);
        }

        /// <summary>
        /// What: candidates with separate target assemblies are scanned in separate fake calls.
        /// </summary>
        [Test]
        public void ApplyNotes_DifferentTargetAssemblies_ScansEachAssemblySeparately()
        {
            HotReloadMethodOutcome first = HotReloadMethodOutcome.Patched("Type.First", "Assets/First.cs");
            HotReloadMethodOutcome second = HotReloadMethodOutcome.Patched("Type.Second", "Assets/Second.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { first, second };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateCandidate("Assembly.One", first),
                    CreateCandidate("Assembly.Two", second)
                };
            List<HotReloadCallSiteScanner.CompiledMethodIdentity[]> calls =
                new List<HotReloadCallSiteScanner.CompiledMethodIdentity[]>();

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                "project",
                outcomes,
                candidates,
                (assemblyName, identities) =>
                {
                    calls.Add(identities);
                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteScanner.CallSiteHit>(),
                        new List<string>());
                });

            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(calls[0].Length, Is.EqualTo(1));
            Assert.That(calls[1].Length, Is.EqualTo(1));
            Assert.That(calls[0][0].AssemblyName, Is.Not.EqualTo(calls[1][0].AssemblyName));
        }

        /// <summary>
        /// What: a worker lifecycle note remains authoritative and skips the caller scan.
        /// </summary>
        [Test]
        public void ApplyNotes_WorkerLifecycleNoteCandidate_SkipsScanAndKeepsNote()
        {
            const string workerNote = "Worker lifecycle note.";
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched(
                "Type.SetUp",
                "Assets/Test.cs",
                workerNote);
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateCandidate("Assembly.One", outcome)
                };
            int scanCount = 0;

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                "project",
                outcomes,
                candidates,
                (assemblyName, identities) =>
                {
                    scanCount++;
                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteScanner.CallSiteHit>(),
                        new List<string>());
                });

            Assert.That(scanCount, Is.EqualTo(0));
            Assert.That(outcomes[0].LifecycleNote, Is.EqualTo(workerNote));
        }

        /// <summary>
        /// What: a proven Awake-only caller writes the full indirect note into the response outcome list.
        /// </summary>
        [Test]
        public void ApplyNotes_OnlyValidAwakeCaller_ReplacesOutcomeWithIndirectLifecycleNote()
        {
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("Type.SetUp", "Assets/Test.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateCandidate(typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name, outcome)
                };
            HotReloadCallSiteScanner.CallSiteHit hit = CreateHit(typeof(ValidLifecycleFixture), "Awake");
            hit.TargetMethodKey = "Type::SetUp()";

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                "project",
                outcomes,
                candidates,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteScanner.CallSiteHit> { hit },
                    new List<string>()));

            Assert.That(
                outcomes[0].LifecycleNote,
                Is.EqualTo(
                    "Type.SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."));
        }

        /// <summary>
        /// What: compiled Awake-only callers add an indirect lifecycle note to a patched outcome.
        /// </summary>
        [Test]
        public void ApplyNotes_CompiledAwakeOnlyCaller_AddsIndirectLifecycleNote()
        {
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("OneShotCallerScannerFixture.AwakeOnlyTarget()", "Assets/Test.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateScannerFixtureCandidate("AwakeOnlyTarget", outcome)
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                projectRoot,
                outcomes,
                candidates,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(
                outcomes[0].LifecycleNote,
                Is.EqualTo(
                    "OneShotCallerScannerFixture.AwakeOnlyTarget() is called only from one-shot lifecycle "
                    + "method(s) (Awake) in the compiled assemblies; objects that already ran them will not run "
                    + "the patched body. It takes effect only for newly created objects, or run `uloop compile` "
                    + "and re-enter Play Mode."));
        }

        /// <summary>
        /// What: a compiled ordinary caller suppresses the indirect lifecycle note.
        /// </summary>
        [Test]
        public void ApplyNotes_CompiledOrdinaryCaller_SuppressesIndirectLifecycleNote()
        {
            HotReloadMethodOutcome outcome = HotReloadMethodOutcome.Patched("OneShotCallerScannerFixture.MixedTarget()", "Assets/Test.cs");
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome> { outcome };
            List<HotReloadOneShotCallerNoteEnricher.Candidate> candidates =
                new List<HotReloadOneShotCallerNoteEnricher.Candidate>
                {
                    CreateScannerFixtureCandidate("MixedTarget", outcome)
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                projectRoot,
                outcomes,
                candidates,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(outcomes[0].LifecycleNote, Is.Empty);
        }

        /// <summary>
        /// What: WithLifecycleNote returns a copy that preserves the patched outcome fields.
        /// </summary>
        [Test]
        public void WithLifecycleNote_PatchedOutcome_PreservesOutcomeFields()
        {
            HotReloadMethodOutcome original = HotReloadMethodOutcome.Patched("Type.Method", "Assets/Test.cs");

            HotReloadMethodOutcome updated = original.WithLifecycleNote("note");

            Assert.That(updated, Is.Not.SameAs(original));
            Assert.That(updated.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(updated.Method, Is.EqualTo("Type.Method"));
            Assert.That(updated.FilePath, Is.EqualTo("Assets/Test.cs"));
            Assert.That(updated.LifecycleNote, Is.EqualTo("note"));
        }
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

        private static HotReloadCallSiteScanner.CallSiteHit CreateHit(Type type, string methodName)
        {
            return CreateHit(type.FullName, methodName);
        }

        private static HotReloadCallSiteScanner.CallSiteHit CreateHit(string typeMetadataName, string methodName)
        {
            return new HotReloadCallSiteScanner.CallSiteHit
            {
                CallerAssemblyName = typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name,
                CallerTypeMetadataName = typeMetadataName,
                CallerMethodName = methodName
            };
        }

        private static HotReloadOneShotCallerNoteEnricher.Candidate CreateCandidate(
            string assemblyName,
            HotReloadMethodOutcome outcome)
        {
            HotReloadCallSiteScanner.CompiledMethodIdentity identity =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    "Type",
                    "SetUp",
                    Array.Empty<string>(),
                    0);
            return new HotReloadOneShotCallerNoteEnricher.Candidate(identity, outcome);
        }

        private static HotReloadOneShotCallerNoteEnricher.Candidate CreateScannerFixtureCandidate(
            string methodName,
            HotReloadMethodOutcome outcome)
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                "Assets/Tests/Editor/HotReload/HotReloadCallSiteScannerFixture.cs");
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            HotReloadCallSiteScanner.CompiledMethodIdentity identity =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    typeof(OneShotCallerScannerFixture).FullName,
                    methodName,
                    Array.Empty<string>(),
                    0);
            return new HotReloadOneShotCallerNoteEnricher.Candidate(identity, outcome);
        }

        private sealed class ValidLifecycleFixture : MonoBehaviour
        {
            private void Awake()
            {
            }
        }

        private sealed class ParameterLifecycleFixture : MonoBehaviour
        {
            private void Awake(int value)
            {
            }
        }

        private sealed class ReturnValueLifecycleFixture : MonoBehaviour
        {
            private int Awake()
            {
                return 1;
            }
        }

        private sealed class NonMonoBehaviourFixture
        {
            private void Awake()
            {
            }
        }
    }
}
