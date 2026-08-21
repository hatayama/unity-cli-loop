using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Pre Using Resolver Added Namespaces behavior.
    /// </summary>
    [TestFixture]
    public class PreUsingResolverAddedNamespacesTests
    {
        [Test]
        public void Resolve_WhenUnresolvedType_ShouldReportAddedNamespace()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Contain("System.Text"));
        }

        [Test]
        public void Resolve_WhenUnresolvedType_ShouldReportAddedAssemblyReference()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedAssemblyReferences, Has.Count.GreaterThan(0));
        }

        [Test]
        public void Resolve_WhenNoMissingUsings_ShouldReportEmptyAddedNamespaces()
        {
            string body = "int x = 42;\nreturn x;";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Is.Empty);
        }

        [Test]
        public void Resolve_WhenMultipleTypes_ShouldReportAllAddedNamespaces()
        {
            string body = "StringBuilder sb = new StringBuilder();\nRegex r = new Regex(\"x\");\nreturn sb.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Contain("System.Text"));
            Assert.That(result.AddedNamespaces, Does.Contain("System.Text.RegularExpressions"));
        }

        [Test]
        public void Resolve_WhenAlreadyHasUsing_ShouldNotReportIt()
        {
            List<string> usings = new() { "using System.Text;" };
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(usings, System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Not.Contain("System.Text"));
        }

        /// <summary>
        /// What: the first unique type identifier that resolved a namespace is recorded as
        /// the speculative trigger.
        /// </summary>
        [Test]
        public void Resolve_WhenUnresolvedType_RecordsTriggerIdentifierForAddedNamespace()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            AutoInjectedNamespace attribution = FindAttribution(result.AddedNamespaceAttributions, "System.Text");
            Assert.That(attribution, Is.Not.Null);
            Assert.That(attribution.TriggerIdentifier, Is.EqualTo("StringBuilder"));
            Assert.That(attribution.IsSpeculative, Is.True);
        }

        /// <summary>
        /// What: identifier System is a known namespace prefix and must not inject
        /// UnityEngine.Rendering.VirtualTexturing.
        /// </summary>
        [Test]
        public void Resolve_WhenIdentifierIsKnownNamespace_DoesNotInjectVirtualTexturing()
        {
            string body = "System.DateTime now = System.DateTime.UtcNow;\nreturn now.Year;";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), System.Array.Empty<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(
                result.AddedNamespaces,
                Does.Not.Contain("UnityEngine.Rendering.VirtualTexturing"));
            Assert.That(
                FindAttribution(result.AddedNamespaceAttributions, "UnityEngine.Rendering.VirtualTexturing"),
                Is.Null);
        }

        /// <summary>
        /// What: System is classified as a known namespace or leading segment.
        /// </summary>
        [Test]
        public void IsKnownNamespaceOrLeadingSegment_WhenIdentifierIsSystem_ReturnsTrue()
        {
            Assert.That(AssemblyTypeIndex.Instance.IsKnownNamespaceOrLeadingSegment("System"), Is.True);
            Assert.That(AssemblyTypeIndex.Instance.IsKnownNamespaceOrLeadingSegment("StringBuilder"), Is.False);
        }

        private static AutoInjectedNamespace FindAttribution(
            IReadOnlyList<AutoInjectedNamespace> attributions,
            string namespaceName)
        {
            foreach (AutoInjectedNamespace attribution in attributions)
            {
                if (attribution.Namespace == namespaceName)
                {
                    return attribution;
                }
            }

            return null;
        }
    }

}
