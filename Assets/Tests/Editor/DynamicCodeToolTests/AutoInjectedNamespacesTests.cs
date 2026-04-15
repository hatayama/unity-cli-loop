using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class PreUsingResolverAddedNamespacesTests
    {
        [Test]
        public void Resolve_WhenUnresolvedType_ShouldReportAddedNamespace()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Contain("System.Text"));
        }

        [Test]
        public void Resolve_WhenUnresolvedType_ShouldReportAddedAssemblyReference()
        {
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedAssemblyReferences, Has.Count.GreaterThan(0));
        }

        [Test]
        public void Resolve_WhenNoMissingUsings_ShouldReportEmptyAddedNamespaces()
        {
            string body = "int x = 42;\nreturn x;";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Is.Empty);
        }

        [Test]
        public void Resolve_WhenMultipleTypes_ShouldReportAllAddedNamespaces()
        {
            string body = "StringBuilder sb = new StringBuilder();\nRegex r = new Regex(\"x\");\nreturn sb.ToString();";
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(), "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Contain("System.Text"));
            Assert.That(result.AddedNamespaces, Does.Contain("System.Text.RegularExpressions"));
        }

        [Test]
        public void Resolve_WhenAlreadyHasUsing_ShouldNotReportIt()
        {
            List<string> usings = new List<string> { "using System.Text;" };
            string body = "StringBuilder builder = new StringBuilder();\nreturn builder.ToString();";
            string wrappedSource = WrapperTemplate.Build(usings, "TestNs", "TestClass", body);

            PreUsingResult result = PreUsingResolver.Resolve(wrappedSource, AssemblyTypeIndex.Instance);

            Assert.That(result.AddedNamespaces, Does.Not.Contain("System.Text"));
        }
    }

    [TestFixture]
    public class AutoInjectedNamespacesIntegrationTests
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
        public async Task CompileAsync_ScriptMode_MissingUsing_ShouldReportAutoInjectedNamespaces()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder builder = new StringBuilder();
                    builder.Append(""hello"");
                    return builder.ToString();
                ",
                ClassName = "AutoInjectedNsTestCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.That(result.AutoInjectedNamespaces, Does.Contain("System.Text"));
        }

        [Test]
        public async Task CompileAsync_ScriptMode_NoMissingUsing_ShouldReportEmptyAutoInjectedNamespaces()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 1 + 2;",
                ClassName = "NoAutoInjectionCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.That(result.AutoInjectedNamespaces, Is.Empty);
        }

        [Test]
        public async Task CompileAsync_ScriptMode_WithExistingUsing_ShouldNotReportIt()
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
                ClassName = "ExistingUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.That(result.AutoInjectedNamespaces, Is.Empty);
        }

        [Test]
        public async Task CompileAsync_ScriptMode_MultipleMissing_ShouldReportAll()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder sb = new StringBuilder();
                    Regex regex = new Regex(@""\d+"");
                    return sb.ToString() + regex.ToString();
                ",
                ClassName = "MultipleAutoInjectedCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Should compile");
            Assert.That(result.AutoInjectedNamespaces, Does.Contain("System.Text"));
            Assert.That(result.AutoInjectedNamespaces, Does.Contain("System.Text.RegularExpressions"));
        }

        [Test]
        public async Task CompileAsync_RawMode_MissingUsing_ShouldReportAutoInjectedNamespaces()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    public class RawModeMissingUsingTest
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
                ClassName = "RawModeMissingUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Raw mode should compile with auto-using");
            Assert.That(result.AutoInjectedNamespaces, Does.Contain("System.Text"));
        }

        [Test]
        public async Task CompileAsync_RawMode_FullyQualifiedNames_ShouldReportEmptyAutoInjectedNamespaces()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    public class RawModeAutoInjectedTest
                    {
                        public async System.Threading.Tasks.Task<object> ExecuteAsync(
                            System.Collections.Generic.Dictionary<string, object> parameters = null,
                            System.Threading.CancellationToken ct = default)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            sb.Append(""raw"");
                            return sb.ToString();
                        }
                    }
                ",
                ClassName = "RawModeAutoInjectedCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success,
                result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Raw mode should compile");
            // Raw mode uses fully-qualified names, so no auto-injection needed
            Assert.That(result.AutoInjectedNamespaces, Is.Empty);
        }

        [Test]
        public async Task CompileAsync_ScriptMode_ShouldPopulateTimings()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 1 + 2;",
                ClassName = "TimingVisibilityCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, "Timing test should compile");
            Assert.That(result.Timings, Has.Count.EqualTo(3));
            Assert.That(result.Timings[0], Does.StartWith("[Perf] ReferenceResolution:"));
            Assert.That(result.Timings[1], Does.StartWith("[Perf] Build:"));
            Assert.That(result.Timings[2], Does.StartWith("[Perf] AssemblyLoad:"));
        }
    }
}
