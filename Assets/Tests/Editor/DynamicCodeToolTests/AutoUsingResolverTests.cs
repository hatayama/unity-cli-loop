using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Auto Using Resolver behavior.
    /// </summary>
    [TestFixture]
    public class AutoUsingResolverTests
    {
        [Test]
        public void AddAssemblyReferenceIfMissing_WhenAssemblyIdentityAlreadyExistsUnderDifferentPath_ShouldSkipDuplicate()
        {
            List<string> currentReferences = new()            {
                "/reference/System.Runtime.dll"
            };
            List<string> assemblyReferencesToAdd = new();

            AutoUsingResolver.AddAssemblyReferenceIfMissing(
                assemblyReferencesToAdd,
                currentReferences,
                "/loaded/System.Runtime.dll");

            Assert.That(assemblyReferencesToAdd, Is.Empty);
        }

        [Test]
        public void AddAssemblyReferenceIfMissing_WhenAssemblyIdentityIsNew_ShouldAddReference()
        {
            List<string> currentReferences = new()            {
                "/reference/System.Runtime.dll"
            };
            List<string> assemblyReferencesToAdd = new();

            AutoUsingResolver.AddAssemblyReferenceIfMissing(
                assemblyReferencesToAdd,
                currentReferences,
                "/loaded/System.Collections.Immutable.dll");

            Assert.That(assemblyReferencesToAdd, Has.Count.EqualTo(1));
            Assert.That(assemblyReferencesToAdd[0], Is.EqualTo("/loaded/System.Collections.Immutable.dll"));
        }

        /// <summary>
        /// What: the first identifier that uniquely maps to a namespace is recorded as
        /// the retry-resolved trigger.
        /// </summary>
        [Test]
        public void RecordFirstUniqueNamespace_WhenCandidateIsUnique_RecordsTriggerIdentifier()
        {
            HashSet<string> addedNamespaces = new();
            List<string> namespacesToAdd = new();
            List<AutoInjectedNamespace> attributions = new();

            AutoUsingResolver.RecordFirstUniqueNamespace(
                "StringBuilder",
                new List<string> { "System.Text" },
                addedNamespaces,
                namespacesToAdd,
                attributions);

            Assert.That(namespacesToAdd, Is.EqualTo(new[] { "System.Text" }));
            Assert.That(attributions.Count, Is.EqualTo(1));
            Assert.That(attributions[0].Namespace, Is.EqualTo("System.Text"));
            Assert.That(attributions[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
            Assert.That(attributions[0].IsSpeculative, Is.False);
        }

        /// <summary>
        /// What: a later identifier for an already-recorded namespace does not replace the
        /// first trigger.
        /// </summary>
        [Test]
        public void RecordFirstUniqueNamespace_WhenNamespaceAlreadyRecorded_KeepsFirstTrigger()
        {
            HashSet<string> addedNamespaces = new();
            List<string> namespacesToAdd = new();
            List<AutoInjectedNamespace> attributions = new();

            AutoUsingResolver.RecordFirstUniqueNamespace(
                "StringBuilder",
                new List<string> { "System.Text" },
                addedNamespaces,
                namespacesToAdd,
                attributions);
            AutoUsingResolver.RecordFirstUniqueNamespace(
                "Encoding",
                new List<string> { "System.Text" },
                addedNamespaces,
                namespacesToAdd,
                attributions);

            Assert.That(attributions.Count, Is.EqualTo(1));
            Assert.That(attributions[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
        }

        /// <summary>
        /// What: ambiguous candidate lists are not injected.
        /// </summary>
        [Test]
        public void RecordFirstUniqueNamespace_WhenCandidatesAreAmbiguous_DoesNotRecord()
        {
            HashSet<string> addedNamespaces = new();
            List<string> namespacesToAdd = new();
            List<AutoInjectedNamespace> attributions = new();

            AutoUsingResolver.RecordFirstUniqueNamespace(
                "System",
                new List<string> { "System", "UnityEngine.Rendering.VirtualTexturing" },
                addedNamespaces,
                namespacesToAdd,
                attributions);

            Assert.That(namespacesToAdd, Is.Empty);
            Assert.That(attributions, Is.Empty);
        }

        /// <summary>
        /// What: the AutoUsingResolver using-injection guard treats System as a known
        /// namespace prefix so VirtualTexturing is not injected for that identifier.
        /// </summary>
        [Test]
        public void KnownNamespaceIdentifier_IsSkippedByUsingInjectionGuard()
        {
            Assert.That(AssemblyTypeIndex.Instance.IsKnownNamespaceOrLeadingSegment("System"), Is.True);
            Assert.That(
                AssemblyTypeIndex.Instance.FindNamespacesForType("System"),
                Does.Contain("UnityEngine.Rendering.VirtualTexturing"));
        }
    }
}
