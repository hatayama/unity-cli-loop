using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Pre Using Resolver Extract Type Identifiers behavior.
    /// </summary>
    [TestFixture]
    public class PreUsingResolverExtractTypeIdentifiersTests
    {
        [Test]
        public void ExtractTypeIdentifiers_WhenUppercaseTypeName_ShouldReturnIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "StringBuilder builder = new StringBuilder();");

            Assert.That(result, Does.Contain("StringBuilder"));
            Assert.That(result, Does.Not.Contain("builder"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenExcludedBuiltInTypes_ShouldSkipThem()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "String x = null; Int32 y = 0; Boolean flag = true;");

            Assert.That(result, Does.Not.Contain("String"));
            Assert.That(result, Does.Not.Contain("Int32"));
            Assert.That(result, Does.Not.Contain("Boolean"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenIdentifierFollowsDot_ShouldSkipIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "System.Text.StringBuilder builder = null;");

            Assert.That(result, Does.Contain("System"));
            Assert.That(result, Does.Not.Contain("Text"));
            Assert.That(result, Does.Not.Contain("StringBuilder"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenStringLiteral_ShouldNotExtractFromIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "string s = \"StringBuilder is great\";");

            Assert.That(result, Does.Not.Contain("StringBuilder"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenLineComment_ShouldNotExtractFromIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "int x = 1; // StringBuilder comment");

            Assert.That(result, Does.Not.Contain("StringBuilder"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenBlockComment_ShouldNotExtractFromIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "int x = 1; /* StringBuilder */ int y = 2;");

            Assert.That(result, Does.Not.Contain("StringBuilder"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenEmpty_ShouldReturnEmptySet()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers("");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenInterpolatedString_ShouldNotExtractFromIt()
        {
            // AdvanceOneTokenPublic skips the entire $"..." including interpolation holes;
            // types inside holes are handled by AutoUsingResolver fallback if needed
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "string s = $\"value is {MyVar} and {\"nested\"}\";");

            Assert.That(result, Does.Not.Contain("MyVar"));
            Assert.That(result, Does.Not.Contain("value"));
            Assert.That(result, Does.Not.Contain("nested"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenGenericTypes_ShouldExtractBothNames()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "HashSet<Regex> set = new HashSet<Regex>();");

            Assert.That(result, Does.Contain("HashSet"));
            Assert.That(result, Does.Contain("Regex"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenMemberInitializer_ShouldSkipIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "new Foo { Name = \"bar\", Count = 1 };");

            Assert.That(result, Does.Contain("Foo"));
            Assert.That(result, Does.Not.Contain("Name"));
            Assert.That(result, Does.Not.Contain("Count"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenNamedArgument_ShouldSkipIt()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "DoSomething(Name: \"bar\");");

            Assert.That(result, Does.Contain("DoSomething"));
            Assert.That(result, Does.Not.Contain("Name"));
        }

        [Test]
        public void ExtractTypeIdentifiers_WhenEqualityComparison_ShouldNotSkip()
        {
            HashSet<string> result = PreUsingResolver.ExtractTypeIdentifiers(
                "if (MyEnum == null) {}");

            Assert.That(result, Does.Contain("MyEnum"));
        }

        [Test]
        public void ExtractQualifiedTypeIdentifiers_WhenFullyQualifiedType_ShouldKeepFullChain()
        {
            HashSet<string> result = PreUsingResolver.ExtractQualifiedTypeIdentifiers(
                "System.Text.StringBuilder builder = new System.Text.StringBuilder();");

            Assert.That(result, Does.Contain("System.Text"));
            Assert.That(result, Does.Contain("System.Text.StringBuilder"));
        }

        [Test]
        public void ExtractQualifiedTypeIdentifiers_WhenUnityRootedType_ShouldKeepUnityChain()
        {
            HashSet<string> result = PreUsingResolver.ExtractQualifiedTypeIdentifiers(
                "UnityEngine.Object.DestroyImmediate(go);");

            Assert.That(result, Does.Contain("UnityEngine.Object"));
        }

        [Test]
        public void FindAssemblyLocationsForIdentifier_WhenQualifiedPrefixIsUnknown_ShouldFallbackToTerminalTypeName()
        {
            List<string> result = AssemblyTypeIndex.Instance.FindAssemblyLocationsForIdentifier(
                "Made.Up.StringBuilder");

            Assert.That(result, Is.Not.Empty);
        }
    }

    /// <summary>
    /// Test fixture that verifies Pre Using Resolver Resolve behavior.
    /// </summary>
    [TestFixture]
    public class PreUsingResolverResolveTests
    {
        [Test]
        public void Resolve_WhenUnresolvedType_ShouldInjectUsing()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.UpdatedSource, Does.Contain("using System.Text;"));
            Assert.IsFalse(ReferenceEquals(result.UpdatedSource, wrappedSource));
        }

        [Test]
        public void Resolve_WhenUnresolvedType_ShouldReportAssemblyReference()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedAssemblyReferences, Has.Count.GreaterThan(0));
        }

        [Test]
        public void Resolve_WhenAlreadyHasUsing_ShouldNotAddDuplicate()
        {
            List<string> usings = new() { "using System.Text;" };
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(usings, "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            int occurrences = CountSubstring(result.UpdatedSource, "using System.Text;");
            Assert.AreEqual(1, occurrences, "Should not add duplicate using System.Text");
        }

        [Test]
        public void Resolve_WhenNoUserTypes_ShouldNotAddSystemText()
        {
            string body = "int x = 42;\nreturn x;";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.UpdatedSource, Does.Not.Contain("using System.Text;"));
        }

        private static int CountSubstring(string source, string target)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(target, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += target.Length;
            }
            return count;
        }

        [Test]
        public void Resolve_WhenMultipleTypes_ShouldInjectAll()
        {
            string body = "StringBuilder sb = new StringBuilder();\nRegex r = new Regex(\"x\");\nreturn sb.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.UpdatedSource, Does.Contain("using System.Text;"));
            Assert.That(result.UpdatedSource, Does.Contain("using System.Text.RegularExpressions;"));
        }

        [Test]
        public void Resolve_WhenFullyQualifiedTypeIsUsed_ShouldReportAssemblyReference()
        {
            string body = "System.Text.StringBuilder builder = new System.Text.StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedAssemblyReferences, Has.Count.GreaterThan(0));
        }
    }
}
