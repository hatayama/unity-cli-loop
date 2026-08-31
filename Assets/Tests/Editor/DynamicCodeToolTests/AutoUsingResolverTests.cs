using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

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

        /// <summary>
        /// What: ResolveAsync does not inject a using or record an attribution when the
        /// CS0246 identifier is a known namespace (the in-loop guard).
        /// </summary>
        [Test]
        public async Task ResolveAsync_WhenCs0246IdentifierIsKnownNamespace_DoesNotInjectOrAttribute()
        {
            AutoUsingResolver resolver = new();
            string sourcePath = Path.Combine(Path.GetTempPath(), "uloop-autousing-known-namespace.cs");
            string dllPath = Path.Combine(Path.GetTempPath(), "uloop-autousing-known-namespace.dll");
            CompilerMessage[] cs0246 = { CreateCs0246("System") };

            try
            {
                AutoUsingResult result = await resolver.ResolveAsync(
                    sourcePath,
                    dllPath,
                    "System.DateTime now = System.DateTime.UtcNow;",
                    new List<string>(),
                    (path, output, references, ct) => Task.FromResult(cs0246),
                    CancellationToken.None);

                Assert.That(result.AddedNamespaces, Is.Empty);
                Assert.That(result.AddedNamespaceAttributions, Is.Empty);
                Assert.That(result.AmbiguousTypeCandidates.ContainsKey("System"), Is.False);
                Assert.That(result.UpdatedSource, Does.Not.Contain("using UnityEngine.Rendering.VirtualTexturing;"));
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }

                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                }
            }
        }

        /// <summary>
        /// What: ResolveAsync records retry attributions for a normal unresolved type
        /// identifier and writes them to AddedNamespaceAttributions.
        /// </summary>
        [Test]
        public async Task ResolveAsync_WhenCs0246IdentifierIsNormalType_RecordsAttribution()
        {
            AutoUsingResolver resolver = new();
            string sourcePath = Path.Combine(Path.GetTempPath(), "uloop-autousing-normal-type.cs");
            string dllPath = Path.Combine(Path.GetTempPath(), "uloop-autousing-normal-type.dll");
            int buildCalls = 0;

            try
            {
                AutoUsingResult result = await resolver.ResolveAsync(
                    sourcePath,
                    dllPath,
                    "StringBuilder builder = new StringBuilder();",
                    new List<string>(),
                    (path, output, references, ct) =>
                    {
                        buildCalls++;
                        if (buildCalls == 1)
                        {
                            return Task.FromResult(new[] { CreateCs0246("StringBuilder") });
                        }

                        return Task.FromResult(Array.Empty<CompilerMessage>());
                    },
                    CancellationToken.None);

                Assert.That(result.AddedNamespaces, Does.Contain("System.Text"));
                Assert.That(result.AddedNamespaceAttributions.Count, Is.EqualTo(1));
                Assert.That(result.AddedNamespaceAttributions[0].Namespace, Is.EqualTo("System.Text"));
                Assert.That(result.AddedNamespaceAttributions[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
                Assert.That(result.AddedNamespaceAttributions[0].IsSpeculative, Is.False);
                Assert.That(result.UpdatedSource, Does.StartWith("using System.Text;"));
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }

                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                }
            }
        }

        private static CompilerMessage CreateCs0246(string identifier)
        {
            return new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "error CS0246: The type or namespace name '" + identifier
                    + "' could not be found (are you missing a using directive or an assembly reference?)"
            };
        }
    }
}
