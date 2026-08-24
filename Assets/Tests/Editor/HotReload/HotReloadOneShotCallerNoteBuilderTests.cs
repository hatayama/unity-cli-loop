using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

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
