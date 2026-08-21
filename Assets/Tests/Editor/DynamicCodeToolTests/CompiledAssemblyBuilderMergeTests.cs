using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies speculative-first merge and first-wins namespace dedup for auto-injected usings.
    /// </summary>
    [TestFixture]
    public sealed class CompiledAssemblyBuilderMergeTests
    {
        /// <summary>
        /// What: speculative attributions are listed before retry-resolved ones when
        /// namespaces do not collide.
        /// </summary>
        [Test]
        public void MergeAutoInjectedNamespaces_WhenNamespacesDiffer_KeepsSpeculativeThenRetry()
        {
            PreUsingResult preUsingResult = CreatePreUsingResult(
                new AutoInjectedNamespace("System.Text", "StringBuilder", true));
            AutoUsingResult autoResult = CreateAutoUsingResult(
                new AutoInjectedNamespace("System.Linq", "Enumerable", false));

            List<AutoInjectedNamespace> merged = CompiledAssemblyBuilder.MergeAutoInjectedNamespaces(
                false,
                preUsingResult,
                autoResult);

            Assert.That(merged.Count, Is.EqualTo(2));
            Assert.That(merged[0].Namespace, Is.EqualTo("System.Text"));
            Assert.That(merged[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
            Assert.That(merged[0].IsSpeculative, Is.True);
            Assert.That(merged[1].Namespace, Is.EqualTo("System.Linq"));
            Assert.That(merged[1].TriggerIdentifier, Is.EqualTo("Enumerable"));
            Assert.That(merged[1].IsSpeculative, Is.False);
        }

        /// <summary>
        /// What: a later retry attribution for the same namespace does not replace the
        /// speculative first-wins entry.
        /// </summary>
        [Test]
        public void MergeAutoInjectedNamespaces_WhenNamespaceCollides_KeepsSpeculativeFirstWins()
        {
            PreUsingResult preUsingResult = CreatePreUsingResult(
                new AutoInjectedNamespace("System.Text", "StringBuilder", true));
            AutoUsingResult autoResult = CreateAutoUsingResult(
                new AutoInjectedNamespace("System.Text", "Encoding", false));

            List<AutoInjectedNamespace> merged = CompiledAssemblyBuilder.MergeAutoInjectedNamespaces(
                false,
                preUsingResult,
                autoResult);

            Assert.That(merged.Count, Is.EqualTo(1));
            Assert.That(merged[0].Namespace, Is.EqualTo("System.Text"));
            Assert.That(merged[0].TriggerIdentifier, Is.EqualTo("StringBuilder"));
            Assert.That(merged[0].IsSpeculative, Is.True);
        }

        /// <summary>
        /// What: a rolled-back speculative using is omitted from the merge so the
        /// response does not report a directive that is no longer in the source.
        /// </summary>
        [Test]
        public void MergeAutoInjectedNamespaces_WhenPreUsingRolledBack_KeepsRetryOnly()
        {
            PreUsingResult preUsingResult = CreatePreUsingResult(
                new AutoInjectedNamespace("System.Text", "StringBuilder", true));
            AutoUsingResult autoResult = CreateAutoUsingResult(
                new AutoInjectedNamespace("System.Linq", "Enumerable", false));

            List<AutoInjectedNamespace> merged = CompiledAssemblyBuilder.MergeAutoInjectedNamespaces(
                true,
                preUsingResult,
                autoResult);

            Assert.That(merged.Count, Is.EqualTo(1));
            Assert.That(merged[0].Namespace, Is.EqualTo("System.Linq"));
            Assert.That(merged[0].TriggerIdentifier, Is.EqualTo("Enumerable"));
            Assert.That(merged[0].IsSpeculative, Is.False);
        }

        private static PreUsingResult CreatePreUsingResult(AutoInjectedNamespace attribution)
        {
            return new PreUsingResult(
                "source",
                new[] { attribution.Namespace },
                Array.Empty<string>(),
                new List<AutoInjectedNamespace> { attribution });
        }

        private static AutoUsingResult CreateAutoUsingResult(AutoInjectedNamespace attribution)
        {
            return new AutoUsingResult(
                "source",
                Array.Empty<CompilerMessage>(),
                new Dictionary<string, List<string>>(),
                new HashSet<string> { attribution.Namespace },
                Array.Empty<string>(),
                new List<AutoInjectedNamespace> { attribution },
                0);
        }
    }
}
