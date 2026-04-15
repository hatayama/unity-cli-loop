using NUnit.Framework;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class DynamicCodeCompilerTests
    {
        private IPreloadAssemblySecurityValidator _previousValidator;

        [SetUp]
        public void SetUp()
        {
            _previousValidator = PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(new SystemReflectionMetadataPreloadValidator());
        }

        [TearDown]
        public void TearDown()
        {
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(_previousValidator);
        }

        [Test]
        public async Task CompileAsync_WhenAssemblyModeIsSelectiveReference_ShouldIgnoreLegacyModeAndSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 1 + 2;",
                ClassName = "SelectiveReferenceCompatibilityCommand",
                Namespace = "TestNamespace",
                AssemblyMode = AssemblyLoadingMode.SelectiveReference
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Compilation should succeed");
            Assert.IsNotNull(result.CompiledAssembly);
        }

        [Test]
        public async Task CompileAsync_Restricted_WhenModuleInitializerExists_ShouldReturnSecurityViolationBeforeLoad()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    using System.Runtime.CompilerServices;

                    public static class DangerousModuleInitializer
                    {
                        [ModuleInitializer]
                        public static void Initialize()
                        {
                            UnityEngine.Debug.Log(""should not run"");
                        }
                    }
                ",
                ClassName = "DangerousModuleInitializerCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsFalse(result.Success, "ModuleInitializer should fail in Restricted mode");
            Assert.IsTrue(result.HasSecurityViolations, "ModuleInitializer should be reported as a security violation");
        }

        [Test]
        public async Task CompileAsync_Restricted_WhenSafeReflectionIsUsed_ShouldSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    System.Reflection.MethodInfo method = typeof(UnityEngine.Mathf).GetMethod(""Max"",
                        new System.Type[] { typeof(float), typeof(float) });
                    object result = method.Invoke(null, new object[] { 3f, 7f });
                    return result;
                ",
                ClassName = "SafeReflectionCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Safe reflection should compile");
            Assert.IsFalse(result.HasSecurityViolations, "Safe reflection should not be flagged");
        }

        [Test]
        public async Task CompileAsync_WhenInterpolatedHoleContainsNestedStringLiteral_ShouldSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    string message = $""x{string.Concat(""}"", ""z"")}y"";
                    return message;
                ",
                ClassName = "NestedInterpolationLiteralCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Interpolated string with nested literals should compile");
            Assert.IsNotNull(result.CompiledAssembly);
        }

        [Test]
        public async Task CompileAsync_WhenInterpolationHoleContainsCollectionInitializer_ShouldSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    string message = $""x{new int[] { 1, 2, 3 }.Length}y"";
                    return message;
                ",
                ClassName = "InterpolationCollectionInitializerCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Interpolated string with collection initializers should compile");
            Assert.IsNotNull(result.CompiledAssembly);
        }

        [Test]
        public async Task CompileAsync_WhenTypeRequiresMissingUsing_ShouldInjectNamespaceAndSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder builder = new StringBuilder();
                    builder.Append(""ok"");
                    return builder.ToString();
                ",
                ClassName = "MissingUsingCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Missing using should be resolved automatically");
            StringAssert.Contains("using System.Text;", result.UpdatedCode);
        }

        [Test]
        public async Task CompileAsync_WhenOnlyLiteralValuesDiffer_ShouldReuseCompiledAssembly()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest firstRequest = new CompilationRequest
            {
                Code = "int benchNonce = 100; return benchNonce;",
                ClassName = "LiteralReuseCommand",
                Namespace = "TestNamespace"
            };
            CompilationRequest secondRequest = new CompilationRequest
            {
                Code = "int benchNonce = 200; return benchNonce;",
                ClassName = "LiteralReuseCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult firstResult = await compiler.CompileAsync(firstRequest, CancellationToken.None);
            CompilationResult secondResult = await compiler.CompileAsync(secondRequest, CancellationToken.None);

            Assert.IsTrue(firstResult.Success, firstResult.Errors != null && firstResult.Errors.Count > 0 ? firstResult.Errors[0].Message : "First compilation should succeed");
            Assert.IsTrue(secondResult.Success, secondResult.Errors != null && secondResult.Errors.Count > 0 ? secondResult.Errors[0].Message : "Second compilation should succeed");
            Assert.AreSame(firstResult.CompiledAssembly, secondResult.CompiledAssembly);
            Assert.That(firstResult.Timings, Has.Some.StartsWith("[Perf] CompilerTotal: "));
        }

        [Test]
        public async Task CompileAsync_WhenConstLiteralRequiresCompileTimeConstant_ShouldFallbackToNonHoistedSource()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    const string message = ""hello"";
                    return message;
                ",
                ClassName = "ConstLiteralFallbackCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.True, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Const literal should compile");
            Assert.That(result.UpdatedCode, Does.Contain("const string message = \"hello\";"));
            Assert.That(result.UpdatedCode, Does.Not.Contain("__uloop_literal_0"));
        }

        [Test]
        public async Task CompileAsync_WhenSwitchCaseLiteralRequiresCompileTimeConstant_ShouldFallbackToNonHoistedSource()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    int value = 2;
                    switch (value)
                    {
                        case 1:
                            return ""one"";
                        case 2:
                            return ""two"";
                    }

                    return ""other"";
                ",
                ClassName = "SwitchLiteralFallbackCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.True, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Switch case literals should compile");
            Assert.That(result.UpdatedCode, Does.Contain("case 1:"));
            Assert.That(result.UpdatedCode, Does.Contain("case 2:"));
            Assert.That(result.UpdatedCode, Does.Not.Contain("__uloop_literal_0"));
        }

        [Test]
        public async Task CompileAsync_WhenAttributeArgumentLiteralRequiresCompileTimeConstant_ShouldFallbackToNonHoistedSource()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    [System.Obsolete(""legacy"")]
                    void Annotated()
                    {
                    }

                    return null;
                ",
                ClassName = "AttributeLiteralFallbackCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.True, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Attribute argument literals should compile");
            Assert.That(result.UpdatedCode, Does.Contain("[System.Obsolete(\"legacy\")]"));
            Assert.That(result.UpdatedCode, Does.Not.Contain("__uloop_literal_0"));
        }

        [Test]
        public async Task CompileAsync_WhenLiteralHoistingFallbackIsUsed_ShouldNotReuseCachedAssembly()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest firstRequest = new CompilationRequest
            {
                Code = @"
                    const string message = ""hello"";
                    return message;
                ",
                ClassName = "ConstLiteralCacheIsolationCommand",
                Namespace = "TestNamespace"
            };
            CompilationRequest secondRequest = new CompilationRequest
            {
                Code = @"
                    const string message = ""world"";
                    return message;
                ",
                ClassName = "ConstLiteralCacheIsolationCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult firstResult = await compiler.CompileAsync(firstRequest, CancellationToken.None);
            CompilationResult secondResult = await compiler.CompileAsync(secondRequest, CancellationToken.None);

            Assert.That(firstResult.Success, Is.True);
            Assert.That(secondResult.Success, Is.True);
            Assert.That(firstResult.UpdatedCode, Does.Contain("const string message = \"hello\";"));
            Assert.That(secondResult.UpdatedCode, Does.Contain("const string message = \"world\";"));
            Assert.That(firstResult.CompiledAssembly, Is.Not.SameAs(secondResult.CompiledAssembly));
        }

        [Test]
        public async Task CompileAsync_WhenReturningCachedAssembly_ShouldReturnDefensiveResultCopies()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 42;",
                ClassName = "CachedResultIsolationCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult firstResult = await compiler.CompileAsync(request, CancellationToken.None);
            CompilationResult secondResult = await compiler.CompileAsync(request, CancellationToken.None);
            CompilationResult thirdResult = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(firstResult.Success, Is.True);
            Assert.That(secondResult.Success, Is.True);
            Assert.That(thirdResult.Success, Is.True);
            Assert.That(secondResult, Is.Not.SameAs(thirdResult));
            Assert.That(secondResult.CompiledAssembly, Is.SameAs(thirdResult.CompiledAssembly));

            secondResult.UpdatedCode = "mutated";

            CompilationResult fourthResult = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(fourthResult.UpdatedCode, Is.Not.EqualTo("mutated"));
        }

        [Test]
        public async Task CompileAsync_WhenReturningCachedAssembly_ShouldPreserveCompilationMetadata()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    StringBuilder builder = new StringBuilder();
                    builder.Append(""ok"");
                    return builder.ToString();
                ",
                ClassName = "CachedMetadataCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult firstResult = await compiler.CompileAsync(request, CancellationToken.None);
            CompilationResult secondResult = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.That(firstResult.Success, Is.True);
            Assert.That(secondResult.Success, Is.True);
            Assert.That(secondResult.UpdatedCode, Is.EqualTo(firstResult.UpdatedCode));
            Assert.That(secondResult.AutoInjectedNamespaces, Is.EquivalentTo(firstResult.AutoInjectedNamespaces));
            Assert.That(secondResult.AutoInjectedNamespaces, Does.Contain("System.Text"));
            Assert.That(firstResult.Timings, Has.No.Member("[Perf] CacheHit: true"));
            Assert.That(firstResult.Timings, Has.Member("[Perf] Backend: SharedRoslynWorker").Or.Member("[Perf] Backend: OneShotRoslyn").Or.Member("[Perf] Backend: AssemblyBuilderFallback"));
            Assert.That(secondResult.Timings, Has.Member("[Perf] CacheHit: true"));
            Assert.That(secondResult.Timings, Has.Member("[Perf] ReferenceResolution: 0.0ms"));
            Assert.That(secondResult.Timings, Has.Member("[Perf] Build: 0.0ms"));
            Assert.That(secondResult.Timings, Has.Member("[Perf] AssemblyLoad: 0.0ms"));
            Assert.That(secondResult.Timings, Has.Member("[Perf] Backend: SharedRoslynWorker").Or.Member("[Perf] Backend: OneShotRoslyn").Or.Member("[Perf] Backend: AssemblyBuilderFallback"));
        }

        [Test]
        public async Task CompileAsync_WhenCustomAsmdefTypeIsReferenced_ShouldAddAssemblyReferenceAndSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    DynamicAssemblyTest test = new DynamicAssemblyTest();
                    return test.HelloWorld();
                ",
                ClassName = "CustomAsmdefAssemblyReferenceCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Custom asmdef type should compile");
            StringAssert.Contains("using io.github.hatayama.uLoopMCP;", result.UpdatedCode);
        }

        [Test]
        public async Task CompileAsync_WhenFullyQualifiedCustomAsmdefTypeIsReferenced_ShouldSucceed()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    io.github.hatayama.uLoopMCP.DynamicAssemblyTest test = new io.github.hatayama.uLoopMCP.DynamicAssemblyTest();
                    return test.HelloWorld();
                ",
                ClassName = "FullyQualifiedCustomAsmdefReferenceCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsTrue(result.Success, result.Errors != null && result.Errors.Count > 0 ? result.Errors[0].Message : "Fully-qualified custom asmdef type should compile");
        }

        [Test]
        public void CompileAsync_WhenCanceledBeforeBuild_ShouldThrowOperationCanceledException()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 1 + 2;",
                ClassName = "CanceledBeforeBuildCommand",
                Namespace = "TestNamespace"
            };

            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await compiler.CompileAsync(request, cts.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task CompileAsync_Restricted_WhenRegistryValidatorRejects_ShouldReturnSecurityViolation()
        {
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(new RejectingPreloadAssemblySecurityValidator());

            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = "return 42;",
                ClassName = "RegistryValidatorCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsFalse(result.Success, "Injected registry validator should block assembly load");
            Assert.IsTrue(result.HasSecurityViolations, "Injected registry validator should surface a security violation");
            Assert.That(result.SecurityViolations, Has.Count.EqualTo(1));
            Assert.That(result.SecurityViolations[0].ApiName, Is.EqualTo("Injected.Validator"));
        }

        [Test]
        public async Task CompileAsync_Restricted_WhenRegistryHasNoValidator_ShouldUseMetadataValidatorFallback()
        {
            PreloadAssemblySecurityValidatorRegistry.SwapValidatorForTests(null);

            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    System.Type type = typeof(System.IO.FileInfo);
                    return type.Name;
                ",
                ClassName = "FallbackMetadataValidatorCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsFalse(result.Success, "Fallback metadata validator should block dangerous type references");
            Assert.IsTrue(result.HasSecurityViolations, "Fallback metadata validator should report a security violation");
            Assert.That(
                result.SecurityViolations.Exists(violation =>
                    violation.Location == "metadata" &&
                    violation.ApiName == "System.IO.FileInfo"),
                Is.True,
                "The rejection should come from the metadata fallback validator");
        }

        [Test]
        public async Task CompileAsync_Restricted_WhenDangerousTypeTokenExists_ShouldReportPreloadIlViolation()
        {
            DynamicCodeCompiler compiler = new DynamicCodeCompiler(DynamicCodeSecurityLevel.Restricted);
            CompilationRequest request = new CompilationRequest
            {
                Code = @"
                    System.Type dangerousType = typeof(System.IO.FileInfo);
                    return dangerousType.Name;
                ",
                ClassName = "PreloadIlViolationCommand",
                Namespace = "TestNamespace"
            };

            CompilationResult result = await compiler.CompileAsync(request, CancellationToken.None);

            Assert.IsFalse(result.Success, "Restricted mode should reject dangerous type tokens before load");
            Assert.IsTrue(result.HasSecurityViolations, "Dangerous type tokens should surface as security violations");
            Assert.That(
                result.SecurityViolations.Exists(violation =>
                    violation.Location == "il" &&
                    violation.ApiName == "System.IO.FileInfo"),
                Is.True,
                "The preload IL validator should report the dangerous type token before Assembly.Load");
        }

        [Test]
        public async Task PreloadIlValidator_WhenDangerousMethodReferenceExists_ShouldReportViolation()
        {
            byte[] assemblyBytes = await BuildAssemblyBytesAsync(
                @"
                    System.Diagnostics.Process.Start(""ls"");
                    return null;
                ",
                "PreloadIlDangerousMethodReferenceCommand");

            PreloadIlSecurityValidator validator = new PreloadIlSecurityValidator();

            SecurityValidationResult result = validator.Validate(assemblyBytes);

            Assert.IsFalse(result.IsValid, "Dangerous IL method references should fail before Assembly.Load");
            Assert.That(
                result.Violations.Exists(violation =>
                    violation.Location == "il" &&
                    violation.ApiName == "System.Diagnostics.Process.Start"),
                Is.True,
                "The preload IL validator should identify dangerous method references from IL tokens");
        }

        [Test]
        public async Task PreloadIlValidator_WhenDangerousReturnTypeExists_ShouldReportViolation()
        {
            byte[] assemblyBytes = await BuildAssemblyBytesAsync(
                @"
                    using System.IO;

                    namespace TestNamespace
                    {
                        public sealed class DangerousReturnTypeContainer
                        {
                            public FileInfo Build()
                            {
                                return null;
                            }
                        }
                    }
                ",
                "PreloadIlDangerousReturnTypeCommand");

            PreloadIlSecurityValidator validator = new PreloadIlSecurityValidator();

            SecurityValidationResult result = validator.Validate(assemblyBytes);

            Assert.IsFalse(result.IsValid, "Dangerous return types should fail before Assembly.Load");
            Assert.That(
                result.Violations.Exists(violation =>
                    violation.Location == "il" &&
                    violation.ApiName == "System.IO.FileInfo"),
                Is.True,
                "The preload IL validator should identify dangerous return types from method signatures");
        }

        [Test]
        public async Task PreloadIlValidator_WhenDangerousGenericArgumentExists_ShouldReportViolation()
        {
            byte[] assemblyBytes = await BuildAssemblyBytesAsync(
                @"
                    using System.Collections.Generic;
                    using System.IO;

                    namespace TestNamespace
                    {
                        public sealed class DangerousGenericReturnTypeContainer
                        {
                            public List<FileInfo> Build()
                            {
                                return null;
                            }
                        }
                    }
                ",
                "PreloadIlDangerousGenericReturnTypeCommand");

            PreloadIlSecurityValidator validator = new PreloadIlSecurityValidator();

            SecurityValidationResult result = validator.Validate(assemblyBytes);

            Assert.IsFalse(result.IsValid, "Dangerous generic arguments should fail before Assembly.Load");
            Assert.That(
                result.Violations.Exists(violation =>
                    violation.Location == "il" &&
                    violation.ApiName == "System.IO.FileInfo"),
                Is.True,
                "The preload IL validator should identify dangerous generic type arguments from method signatures");
        }

        [Test]
        public async Task PreloadIlValidator_WhenAssemblyIsSafe_ShouldRemainValid()
        {
            byte[] assemblyBytes = await BuildAssemblyBytesAsync(
                "return 42;",
                "PreloadIlSafeAssemblyCommand");

            PreloadIlSecurityValidator validator = new PreloadIlSecurityValidator();

            SecurityValidationResult result = validator.Validate(assemblyBytes);

            Assert.IsTrue(result.IsValid, "Safe assemblies should not fail preload IL validation");
            Assert.That(result.Violations, Is.Empty);
        }

        private static async Task<byte[]> BuildAssemblyBytesAsync(string code, string className)
        {
            CompilationRequest request = new CompilationRequest
            {
                Code = code,
                ClassName = className,
                Namespace = "TestNamespace"
            };

            DynamicCompilationPlan plan = DynamicCodeServices.CompilationPlanner.CreatePlan(request);
            CompiledAssemblyBuildResult buildResult = await DynamicCodeServices.AssemblyBuilder.BuildAsync(
                plan,
                CancellationToken.None);

            Assert.That(buildResult.Diagnostics.Errors, Is.Empty, "The helper assembly should compile cleanly for validator tests");
            Assert.That(buildResult.AssemblyBytes, Is.Not.Null);
            return buildResult.AssemblyBytes;
        }

        private sealed class RejectingPreloadAssemblySecurityValidator : IPreloadAssemblySecurityValidator
        {
            public SecurityValidationResult Validate(byte[] assemblyBytes)
            {
                return new SecurityValidationResult
                {
                    IsValid = false,
                    Violations = new List<SecurityViolation>
                    {
                        new SecurityViolation
                        {
                            Type = SecurityViolationType.DangerousApiCall,
                            ApiName = "Injected.Validator",
                            Message = "Injected validator rejected the assembly",
                            Description = "The test validator forces the compiler to use the registry result.",
                            Location = "test"
                        }
                    }
                };
            }
        }
    }
}
