using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
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
            List<string> usings = new List<string> { "using System.Text;" };
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

    [TestFixture]
    public class PreUsingResolverIntegrationTests
    {
        private IPreloadAssemblySecurityValidator _previousValidator;

        [SetUp]
        public void SetUp()
        {
            _previousValidator = PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(
                new SystemReflectionMetadataPreloadValidator());
        }

        [TearDown]
        public void TearDown()
        {
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(_previousValidator);
        }

        [Test]
        public async Task CompileAsync_ScriptMode_MissingUsing_ShouldSucceedWithSingleBuild()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder builder = new StringBuilder();
                    builder.Append(""hello"");
                    return builder.ToString();
                ",
                ClassName = "PreUsingMissingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            StringAssert.Contains("using System.Text;", result.UpdatedCode);
            // PreUsingResolver pre-injects the using, so AutoUsingResolver needs no retry
            Assert.AreEqual(1, compiler.LastBuildCount, "Should compile in a single build (no retry)");
        }

        [Test]
        public async Task CompileAsync_RawMode_FullClass_ShouldSucceedWithoutPreUsingIntervention()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    using System.Text;

                    public class RawModeTestClass
                    {
                        public async System.Threading.Tasks.Task<object> ExecuteAsync(
                            System.Collections.Generic.Dictionary<string, object> parameters = null,
                            System.Threading.CancellationToken ct = default)
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.Append(""raw"");
                            return sb.ToString();
                        }
                    }
                ",
                ClassName = "RawModeCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Raw mode should compile");
            Assert.IsNotNull(result.CompiledAssembly);
        }

        [Test]
        public async Task CompileAsync_ScriptMode_AllUsingsPresent_ShouldCompileNormally()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    using System.Text;
                    StringBuilder builder = new StringBuilder();
                    builder.Append(""already imported"");
                    return builder.ToString();
                ",
                ClassName = "AlreadyImportedCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.AreEqual(1, compiler.LastBuildCount, "No retry needed when using is already present");
        }

        [Test]
        public async Task CompileAsync_ScriptMode_MultipleMissingUsings_ShouldPreInjectAllAndSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder sb = new StringBuilder();
                    Regex regex = new Regex(@""\d+"");
                    return sb.ToString() + regex.ToString();
                ",
                ClassName = "MultiplePreUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            StringAssert.Contains("using System.Text;", result.UpdatedCode);
            StringAssert.Contains("using System.Text.RegularExpressions;", result.UpdatedCode);
            Assert.AreEqual(1, compiler.LastBuildCount, "Both usings pre-injected, no retry needed");
        }

        [Test]
        public async Task CompileAsync_ScriptMode_SimpleArithmetic_ShouldSucceedWithSingleBuild()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 1 + 2;",
                ClassName = "SimpleArithmeticPreUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.IsNotNull(result.CompiledAssembly);
            Assert.AreEqual(1, compiler.LastBuildCount, "Simple code needs no retry");
        }

        [Test]
        public async Task CompileAsync_ScriptMode_CustomAsmdefType_ShouldResolveAssemblyReference()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    DynamicAssemblyTest test = new DynamicAssemblyTest();
                    return test.HelloWorld();
                ",
                ClassName = "CustomAsmdefReferenceCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Custom asmdef type should compile");
            Assert.AreEqual(1, compiler.LastBuildCount, "Unique type resolution should avoid extra retries");
        }

        [Test]
        public async Task CompileAsync_ScriptMode_FullyQualifiedCustomAsmdefType_ShouldResolveWithoutRetry()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    io.github.hatayama.uLoopMCP.DynamicAssemblyTest test = new io.github.hatayama.uLoopMCP.DynamicAssemblyTest();
                    return test.HelloWorld();
                ",
                ClassName = "FullyQualifiedCustomAsmdefPreUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Fully-qualified custom asmdef type should compile");
            Assert.AreEqual(1, compiler.LastBuildCount, "Qualified assembly resolution should avoid extra retries");
        }
    }
}
