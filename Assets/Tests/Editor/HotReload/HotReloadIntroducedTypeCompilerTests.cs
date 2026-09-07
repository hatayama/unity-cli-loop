using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers compiler rejection before an artifact can be loaded or registered.
    /// </summary>
    public class HotReloadIntroducedTypeCompilerTests
    {
        /// <summary>
        /// Verifies that compiler path resolution happens before compilation and loading.
        /// </summary>
        [Test]
        public async Task CompileAsync_PathsUnavailable_DoesNotCompileOrLoad()
        {
            FakeEnvironment environment = new FakeEnvironment { PathsAvailable = false };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.CompileCalls, Is.EqualTo(0));
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that fallback output is rejected before loading an irreversible assembly.
        /// </summary>
        [Test]
        public async Task CompileAsync_FallbackBackend_DoesNotLoad()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                BackendKind = DynamicCompilationBackendKind.AssemblyBuilderFallback
            };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that an error diagnostic rejects a residual DLL before the loader is called.
        /// </summary>
        [Test]
        public async Task CompileAsync_ErrorDiagnosticWithDll_DoesNotLoad()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                CompilerMessages = new[]
                {
                    new CompilerMessage
                    {
                        type = CompilerMessageType.Error,
                        message = "compiler error"
                    }
                }
            };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a missing portable PDB prevents loading the generated DLL.
        /// </summary>
        [Test]
        public async Task CompileAsync_MissingPdb_DoesNotLoad()
        {
            FakeEnvironment environment = new FakeEnvironment { PdbExists = false };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a DLL identity mismatch is rejected before its bytes are loaded.
        /// </summary>
        [Test]
        public async Task CompileAsync_AssemblyIdentityMismatch_DoesNotLoad()
        {
            FakeEnvironment environment = new FakeEnvironment { IdentityMatches = false };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a missing requested type definition is rejected before loading.
        /// </summary>
        [Test]
        public async Task CompileAsync_MissingRequestedTypeDefinition_DoesNotLoad()
        {
            FakeEnvironment environment = new FakeEnvironment { ContainsRequestedType = false };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a fully validated output produces a prepared artifact without activation.
        /// </summary>
        [Test]
        public async Task CompileAsync_ValidatedOutput_ReturnsPreparedArtifact()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                CreateRequest(),
                CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Artifact, Is.Not.Null);
            Assert.That(environment.LoadCalls, Is.EqualTo(1));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an error emitted for a later source retains that descriptor's owner.
        /// </summary>
        [Test]
        public async Task CompileAsync_LaterSourceError_RetainsOwnerDiagnostic()
        {
            HotReloadIntroducedTypeDescriptor first = CreateDescriptor("Example.First", "Assets/First.cs");
            HotReloadIntroducedTypeDescriptor second = CreateDescriptor("Example.Second", "Assets/Second.cs");
            HotReloadIntroducedTypeCompilationRequest request =
                new HotReloadIntroducedTypeCompilationRequest(
                    new[]
                    {
                        new HotReloadIntroducedTypeSource("first.cs", first),
                        new HotReloadIntroducedTypeSource("second.cs", second)
                    },
                    "artifact.dll",
                    "artifact.pdb",
                    typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                    new[] { first, second },
                    Array.Empty<string>(),
                    Array.Empty<string>());
            FakeEnvironment environment = new FakeEnvironment
            {
                CompilerMessages = new[]
                {
                    new CompilerMessage
                    {
                        type = CompilerMessageType.Error,
                        file = "second.cs",
                        message = "second source error"
                    }
                }
            };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                request,
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].OwnerProjectRelativePath, Is.EqualTo("Assets/Second.cs"));
            Assert.That(result.Diagnostics[0].Message, Is.EqualTo("second source error"));
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a real Roslyn error in the second emitted source retains its descriptor
        /// owner instead of being reported only as a batch-level failure.
        /// </summary>
        [Test]
        public async Task CompileAsync_ProductionLaterSourceError_RetainsOwnerDiagnostic()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifactPaths paths =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-diagnostic-tests").Create();
            HotReloadIntroducedTypeDescriptor first = new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                "CompilerDiagnostic.First",
                "Assets/First.cs",
                "first",
                "namespace CompilerDiagnostic { public class First { } }");
            HotReloadIntroducedTypeDescriptor second = new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                "CompilerDiagnostic.Second",
                "Assets/Second.cs",
                "second",
                "namespace CompilerDiagnostic { public class Second { public MissingType Value; } }");
            HotReloadIntroducedTypeCompilationRequest request =
                new HotReloadIntroducedTypeCompilationRequest(
                    new[]
                    {
                        new HotReloadIntroducedTypeSource(paths.CreateSourcePath(0), first),
                        new HotReloadIntroducedTypeSource(paths.CreateSourcePath(1), second)
                    },
                    paths.DllPath,
                    paths.PdbPath,
                    paths.AssemblyFullName,
                    new[] { first, second },
                    CreateReferencePaths(),
                    Array.Empty<string>());
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment());

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                request,
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Artifact, Is.Null);
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].OwnerProjectRelativePath, Is.EqualTo("Assets/Second.cs"));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("MissingType"));
        }

        /// <summary>
        /// Verifies that invalid later source records are rejected before any source can be written.
        /// </summary>
        [Test]
        public void CompilationRequest_DuplicateOrNullSources_RejectsBeforeWrites()
        {
            HotReloadIntroducedTypeDescriptor descriptor = CreateDescriptor("Example.Introduced", "Assets/Example.cs");
            FakeEnvironment environment = new FakeEnvironment();

            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeCompilationRequest(
                new[]
                {
                    new HotReloadIntroducedTypeSource("duplicate.cs", descriptor),
                    new HotReloadIntroducedTypeSource("duplicate.cs", descriptor)
                },
                "artifact.dll",
                "artifact.pdb",
                typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                new[] { descriptor },
                Array.Empty<string>(),
                Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeCompilationRequest(
                new HotReloadIntroducedTypeSource[]
                {
                    new HotReloadIntroducedTypeSource("first.cs", descriptor),
                    null
                },
                "artifact.dll",
                "artifact.pdb",
                typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                new[] { descriptor },
                Array.Empty<string>(),
                Array.Empty<string>()));

            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that two source paths differing only in case are rejected before either is
        /// written, because a case-insensitive volume resolves both spellings to one file.
        /// </summary>
        [Test]
        public void CompilationRequest_CaseOnlyDuplicateSources_RejectsBeforeWrites()
        {
            HotReloadIntroducedTypeDescriptor descriptor = CreateDescriptor("Example.Introduced", "Assets/Example.cs");
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadIntroducedTypeSource[] sources =
            {
                new HotReloadIntroducedTypeSource("Duplicate.cs", descriptor),
                new HotReloadIntroducedTypeSource("duplicate.cs", descriptor)
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => CreateRequestFrom(sources));

            Assert.That(exception.ParamName, Is.EqualTo("sources"));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a batch carrying two descriptors with one identity is rejected before any
        /// source is written or any assembly is loaded.
        /// </summary>
        [Test]
        public void CompilationRequest_DuplicateDescriptorIdentity_RejectsBeforeWrites()
        {
            HotReloadIntroducedTypeDescriptor first = CreateDescriptor("Example.Introduced", "Assets/Example.cs");
            HotReloadIntroducedTypeDescriptor second = CreateDescriptor("Example.Introduced", "Assets/Other.cs");
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadIntroducedTypeSource[] sources =
            {
                new HotReloadIntroducedTypeSource("first.cs", first),
                new HotReloadIntroducedTypeSource("second.cs", second)
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => CreateRequestFrom(sources));

            Assert.That(exception.ParamName, Is.EqualTo("descriptors"));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a descriptor list whose count differs from the source list is rejected,
        /// so a source can never be compiled without the descriptor it belongs to.
        /// </summary>
        [Test]
        public void CompilationRequest_DescriptorCountMismatch_RejectsBeforeWrites()
        {
            HotReloadIntroducedTypeDescriptor first = CreateDescriptor("Example.First", "Assets/First.cs");
            HotReloadIntroducedTypeDescriptor second = CreateDescriptor("Example.Second", "Assets/Second.cs");
            FakeEnvironment environment = new FakeEnvironment();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeCompilationRequest(
                    new[] { new HotReloadIntroducedTypeSource("first.cs", first) },
                    "artifact.dll",
                    "artifact.pdb",
                    typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                    new[] { first, second },
                    Array.Empty<string>(),
                    Array.Empty<string>()));

            Assert.That(exception.ParamName, Is.EqualTo("sources"));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a source paired with a descriptor at another position is rejected, so the
        /// diagnostics that map a source path back to its owner cannot name the wrong file.
        /// </summary>
        [Test]
        public void CompilationRequest_MisorderedDescriptors_RejectsBeforeWrites()
        {
            HotReloadIntroducedTypeDescriptor first = CreateDescriptor("Example.First", "Assets/First.cs");
            HotReloadIntroducedTypeDescriptor second = CreateDescriptor("Example.Second", "Assets/Second.cs");
            FakeEnvironment environment = new FakeEnvironment();

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new HotReloadIntroducedTypeCompilationRequest(
                    new[]
                    {
                        new HotReloadIntroducedTypeSource("first.cs", first),
                        new HotReloadIntroducedTypeSource("second.cs", second)
                    },
                    "artifact.dll",
                    "artifact.pdb",
                    typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                    new[] { second, first },
                    Array.Empty<string>(),
                    Array.Empty<string>()));

            Assert.That(exception.ParamName, Is.EqualTo("sources"));
            Assert.That(environment.WriteSourceCalls, Is.EqualTo(0));
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a descriptor with an empty identity part is rejected at construction,
        /// because BuildIdentity would otherwise collapse distinct declarations onto one key.
        /// </summary>
        [Test]
        public void Descriptor_EmptyIdentityPart_IsRejected()
        {
            Assert.That(
                Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeDescriptor(
                    string.Empty,
                    "original-mvid",
                    "Example.Introduced",
                    "Assets/Example.cs",
                    "fingerprint",
                    "public class Introduced { }")).ParamName,
                Is.EqualTo("originalAssemblyName"));
            Assert.That(
                Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeDescriptor(
                    "OriginalAssembly",
                    null,
                    "Example.Introduced",
                    "Assets/Example.cs",
                    "fingerprint",
                    "public class Introduced { }")).ParamName,
                Is.EqualTo("originalAssemblyMvid"));
            Assert.That(
                Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeDescriptor(
                    "OriginalAssembly",
                    "original-mvid",
                    "   ",
                    "Assets/Example.cs",
                    "fingerprint",
                    "public class Introduced { }")).ParamName,
                Is.EqualTo("metadataName"));
            Assert.That(
                Assert.Throws<ArgumentException>(() => new HotReloadIntroducedTypeDescriptor(
                    "OriginalAssembly",
                    "original-mvid",
                    "Example.Introduced",
                    "Assets/Example.cs",
                    string.Empty,
                    "public class Introduced { }")).ParamName,
                Is.EqualTo("declarationFingerprint"));
        }

        /// <summary>
        /// Verifies that a request copies its source records so later caller mutations cannot
        /// change the batch that awaits compilation.
        /// </summary>
        [Test]
        public void CompilationRequest_SourceCollectionMutation_DoesNotChangeRequest()
        {
            HotReloadIntroducedTypeDescriptor first = CreateDescriptor("Example.First", "Assets/First.cs");
            HotReloadIntroducedTypeDescriptor second = CreateDescriptor("Example.Second", "Assets/Second.cs");
            List<HotReloadIntroducedTypeSource> sources = new List<HotReloadIntroducedTypeSource>
            {
                new HotReloadIntroducedTypeSource("first.cs", first),
                new HotReloadIntroducedTypeSource("second.cs", second)
            };
            HotReloadIntroducedTypeCompilationRequest request = new HotReloadIntroducedTypeCompilationRequest(
                sources,
                "artifact.dll",
                "artifact.pdb",
                typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                new[] { first, second },
                Array.Empty<string>(),
                Array.Empty<string>());
            sources[1] = new HotReloadIntroducedTypeSource("changed.cs", second);

            Assert.That(request.Sources[1].Path, Does.EndWith("second.cs"));
            Assert.That(request.Sources[1].Text, Is.EqualTo(second.Source));
        }

        /// <summary>
        /// Verifies that result factories reject contradictory internal success and failure
        /// states instead of constructing a result every caller must defensively reinterpret.
        /// </summary>
        [Test]
        public void CompilerResult_InvalidFactoryArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => HotReloadIntroducedTypeCompilerResult.Prepared(null));
            Assert.Throws<ArgumentException>(() => HotReloadIntroducedTypeCompilerResult.Failure(string.Empty));
            Assert.Throws<ArgumentException>(() => HotReloadIntroducedTypeCompilerResult.Failure("  "));
        }

        /// <summary>
        /// Verifies that cancellation after backend completion prevents the irreversible loader
        /// call even when every emitted output validation would otherwise succeed.
        /// </summary>
        [Test]
        public async Task CompileAsync_CancelledAfterBackend_DoesNotLoad()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            FakeEnvironment environment = new FakeEnvironment
            {
                AfterCompile = cancellation.Cancel
            };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            bool cancelled = false;
            try
            {
                await compiler.CompileAsync(CreateRequest(), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that cancellation occurring while artifact bytes are read is rechecked
        /// immediately before loading and cannot begin the irreversible loader operation.
        /// </summary>
        [Test]
        public async Task CompileAsync_CancelledDuringByteReads_DoesNotLoad()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            FakeEnvironment environment = new FakeEnvironment
            {
                AfterFirstReadAllBytes = cancellation.Cancel
            };
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(environment);

            bool cancelled = false;
            try
            {
                await compiler.CompileAsync(CreateRequest(), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True);
            Assert.That(environment.LoadCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that the production Roslyn environment writes a uniquely named DLL/PDB pair,
        /// honors defines, and returns a loaded artifact containing every requested type.
        /// </summary>
        [Test]
        public async Task CompileAsync_ProductionEnvironment_ProducesDefinedArtifact()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifactPathFactory factory =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-tests");
            HotReloadIntroducedTypeArtifactPaths paths = factory.Create();
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "CompilerFixture.One.First",
                        "Assets/First.cs",
                        "first",
                        "namespace CompilerFixture.One {\nusing Alias = System.IDisposable;\n#if INTRODUCED_DEFINE\npublic class First { public Alias Create() { return null; } }\n#endif\n}"),
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "CompilerFixture.Two.ISecond",
                        "Assets/Second.cs",
                        "second",
                        "namespace CompilerFixture.Two { using Alias = System.ICloneable; public interface ISecond { Alias Create(); } }"),
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "CompilerFixture.Initializer",
                        "Assets/Initializer.cs",
                        "initializer",
                        "namespace CompilerFixture { public class Initializer { static Initializer() { throw new System.InvalidOperationException(); } } }")
                };
            HotReloadIntroducedTypeCompilationRequest request =
                HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                    paths,
                    descriptors,
                    CreateReferencePaths(),
                    new[] { "INTRODUCED_DEFINE" });
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment());

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                request,
                CancellationToken.None);

            try
            {
                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.Artifact, Is.Not.Null);
                Assert.That(File.Exists(paths.DllPath), Is.True);
                Assert.That(File.Exists(paths.PdbPath), Is.True);
                Assert.That(result.Artifact.Assembly.GetType("CompilerFixture.One.First"), Is.Not.Null);
                Assert.That(result.Artifact.Assembly.GetType("CompilerFixture.Two.ISecond"), Is.Not.Null);
                Assert.That(result.Artifact.Assembly.GetType("CompilerFixture.Initializer"), Is.Not.Null);
            }
            finally
            {
                DeleteArtifactDirectory(paths.DllPath);
            }
        }

        /// <summary>
        /// Verifies that emitted-type validation reads nested definitions in their Cecil metadata
        /// form, so a descriptor is matched by the name the artifact actually defines.
        /// </summary>
        [Test]
        public async Task CompileAsync_NestedDefinition_MatchesCecilMetadataName()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifactPaths paths =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-nested-tests").Create();
            HotReloadIntroducedTypeDescriptor nested = new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                "NestedFixture.Outer/Inner",
                "Assets/Nested.cs",
                "nested",
                "namespace NestedFixture { public class Outer { public class Inner { } } }");
            HotReloadIntroducedTypeCompilationRequest request =
                new HotReloadIntroducedTypeCompilationRequest(
                    new[] { new HotReloadIntroducedTypeSource(paths.CreateSourcePath(0), nested) },
                    paths.DllPath,
                    paths.PdbPath,
                    paths.AssemblyFullName,
                    new[] { nested },
                    CreateReferencePaths(),
                    Array.Empty<string>());
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment());

            HotReloadIntroducedTypeCompilerResult result = await compiler.CompileAsync(
                request,
                CancellationToken.None);

            try
            {
                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.Artifact, Is.Not.Null);
                Assert.That(result.Artifact.Assembly.GetType("NestedFixture.Outer+Inner"), Is.Not.Null);
            }
            finally
            {
                DeleteArtifactDirectory(paths.DllPath);
            }
        }

        /// <summary>
        /// What: a batch whose type derives from a type an earlier artifact already holds compiles
        /// only when that artifact is among the reference paths, so the retained-artifact
        /// references a run collects are what makes a continuation batch buildable at all.
        /// </summary>
        [Test]
        public async Task CompileAsync_ContinuationBatch_NeedsTheEarlierArtifactReference()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifactPaths firstPaths =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-continuation-first").Create();
            HotReloadIntroducedTypeArtifactPaths withoutReferencePaths =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-continuation-without").Create();
            HotReloadIntroducedTypeArtifactPaths withReferencePaths =
                new HotReloadIntroducedTypeArtifactPathFactory(projectRoot, "compiler-continuation-with").Create();
            HotReloadIntroducedTypeCompiler compiler = new HotReloadIntroducedTypeCompiler(
                new HotReloadIntroducedTypeCompilerEnvironment());

            try
            {
                HotReloadIntroducedTypeCompilerResult first = await compiler.CompileAsync(
                    CreateContinuationRequest(
                        firstPaths,
                        "ContinuationFixture.Base",
                        "namespace ContinuationFixture { public class Base { } }",
                        CreateReferencePaths()),
                    CancellationToken.None);
                Assert.That(first.Success, Is.True, first.ErrorMessage);

                List<string> referencesWithArtifact = new List<string>(CreateReferencePaths());
                HotReloadShimReferenceBuilder.AppendIntroducedTypeArtifactReferences(
                    referencesWithArtifact,
                    new[]
                    {
                        new TransformWorkerIntroducedTypeArtifactDto
                        {
                            assemblyFullName = firstPaths.AssemblyFullName,
                            referencePath = firstPaths.DllPath,
                            types = Array.Empty<TransformWorkerIntroducedTypeArtifactTypeDto>()
                        }
                    });

                HotReloadIntroducedTypeCompilerResult withoutReference = await compiler.CompileAsync(
                    CreateContinuationRequest(
                        withoutReferencePaths,
                        "ContinuationFixture.Derived",
                        "namespace ContinuationFixture { public class Derived : Base { } }",
                        CreateReferencePaths()),
                    CancellationToken.None);
                HotReloadIntroducedTypeCompilerResult withReference = await compiler.CompileAsync(
                    CreateContinuationRequest(
                        withReferencePaths,
                        "ContinuationFixture.Derived",
                        "namespace ContinuationFixture { public class Derived : Base { } }",
                        referencesWithArtifact.ToArray()),
                    CancellationToken.None);

                Assert.That(withoutReference.Success, Is.False);
                Assert.That(withReference.Success, Is.True, withReference.ErrorMessage);
                Assert.That(withReference.Artifact.Assembly.GetType("ContinuationFixture.Derived"), Is.Not.Null);
            }
            finally
            {
                DeleteArtifactDirectory(firstPaths.DllPath);
                DeleteArtifactDirectory(withoutReferencePaths.DllPath);
                DeleteArtifactDirectory(withReferencePaths.DllPath);
            }
        }

        private static HotReloadIntroducedTypeCompilationRequest CreateContinuationRequest(
            HotReloadIntroducedTypeArtifactPaths paths,
            string metadataName,
            string source,
            string[] referencePaths)
        {
            HotReloadIntroducedTypeDescriptor descriptor = new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                metadataName,
                "Assets/Continuation.cs",
                "continuation",
                source);
            return HotReloadIntroducedTypeCompilationRequest.CreateBatch(
                paths,
                new[] { descriptor },
                referencePaths,
                Array.Empty<string>());
        }

        // The artifact assembly is loaded from bytes rather than mapped from the file, so its
        // directory can be removed as soon as the assertions have read the emitted DLL and PDB.
        private static void DeleteArtifactDirectory(string dllPath)
        {
            string directory = Path.GetDirectoryName(dllPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException exception)
            {
                // A leftover artifact directory must not turn into a test failure of its own.
                UnityEngine.Debug.Log("Artifact directory could not be removed: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                UnityEngine.Debug.Log("Artifact directory could not be removed: " + exception.Message);
            }
        }

        private static string[] CreateReferencePaths()
        {
            UnityEditor.Compilation.Assembly targetAssembly = null;
            string testAssemblyName = typeof(HotReloadIntroducedTypeCompilerTests).Assembly.GetName().Name;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == testAssemblyName)
                {
                    targetAssembly = assembly;
                    break;
                }
            }

            Assert.That(targetAssembly, Is.Not.Null, "Test compilation assembly must exist.");
            List<string> references = new List<string>();
            foreach (string referencePath in targetAssembly.allReferences)
            {
                if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath))
                {
                    references.Add(Path.GetFullPath(referencePath));
                }
            }

            string assemblyPath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Library",
                "ScriptAssemblies",
                testAssemblyName + ".dll");
            if (!references.Contains(assemblyPath))
            {
                references.Add(assemblyPath);
            }

            return references.ToArray();
        }

        private static HotReloadIntroducedTypeCompilationRequest CreateRequestFrom(
            IReadOnlyList<HotReloadIntroducedTypeSource> sources)
        {
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>();
            foreach (HotReloadIntroducedTypeSource source in sources)
            {
                descriptors.Add(source.Descriptor);
            }

            return new HotReloadIntroducedTypeCompilationRequest(
                sources,
                "artifact.dll",
                "artifact.pdb",
                typeof(HotReloadIntroducedTypeCompilerTests).Assembly.FullName,
                descriptors,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static HotReloadIntroducedTypeCompilationRequest CreateRequest()
        {
            AssemblyName assemblyName = typeof(HotReloadIntroducedTypeCompilerTests).Assembly.GetName();
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        "Assets/Example.cs",
                        "fingerprint",
                        "public class Introduced { }")
                };
            HotReloadIntroducedTypeDescriptor descriptor = descriptors[0];
            return new HotReloadIntroducedTypeCompilationRequest(
                new[] { new HotReloadIntroducedTypeSource("source.cs", descriptor) },
                "artifact.dll",
                "artifact.pdb",
                assemblyName.FullName,
                descriptors,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(
            string metadataName,
            string ownerProjectRelativePath)
        {
            return new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                metadataName,
                ownerProjectRelativePath,
                "fingerprint",
                "public class Introduced { }");
        }

        private sealed class FakeEnvironment : IHotReloadIntroducedTypeCompilerEnvironment
        {
            public bool PathsAvailable { get; set; } = true;

            public DynamicCompilationBackendKind BackendKind { get; set; } =
                DynamicCompilationBackendKind.SharedRoslynWorker;

            public bool DllExists { get; set; } = true;

            public bool PdbExists { get; set; } = true;

            public bool IdentityMatches { get; set; } = true;

            public bool ContainsRequestedType { get; set; } = true;

            public CompilerMessage[] CompilerMessages { get; set; } = Array.Empty<CompilerMessage>();

            public int CompileCalls { get; private set; }

            public int LoadCalls { get; private set; }

            public int WriteSourceCalls { get; private set; }

            public Action AfterCompile { get; set; }

            public Action AfterFirstReadAllBytes { get; set; }

            public int ReadAllBytesCalls { get; private set; }

            public Task<ExternalCompilerPaths> ResolveCompilerPathsOnMainThreadAsync(CancellationToken ct)
            {
                ExternalCompilerPaths paths = PathsAvailable
                    ? new ExternalCompilerPaths(
                        "contents",
                        "scripting",
                        "dotnet",
                        "compiler",
                        "runtimeconfig",
                        "deps",
                        "codeanalysis",
                        "codeanalysiscsharp",
                        "shared",
                        ExternalCompilerLayoutKind.Unknown)
                    : null;
                return Task.FromResult(paths);
            }

            public Task<DynamicCompilationBackendResult> CompileAsync(
                HotReloadIntroducedTypeCompilationRequest request,
                ExternalCompilerPaths paths,
                CancellationToken ct)
            {
                CompileCalls++;
                AfterCompile?.Invoke();
                DynamicCompilationBackendResult result = new DynamicCompilationBackendResult(
                    CompilerMessages,
                    BackendKind);
                return Task.FromResult(result);
            }

            public bool FileExists(string path)
            {
                return path.EndsWith(".pdb", StringComparison.Ordinal) ? PdbExists : DllExists;
            }

            public AssemblyName ReadAssemblyName(string path)
            {
                return IdentityMatches
                    ? typeof(HotReloadIntroducedTypeCompilerTests).Assembly.GetName()
                    : new AssemblyName("UnexpectedArtifact");
            }

            public byte[] ReadAllBytes(string path)
            {
                ReadAllBytesCalls++;
                if (ReadAllBytesCalls == 1)
                {
                    AfterFirstReadAllBytes?.Invoke();
                }

                return new byte[] { 1 };
            }

            public CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes)
            {
                LoadCalls++;
                return new CompiledAssemblyLoadResult(
                    true,
                    typeof(HotReloadIntroducedTypeCompilerTests).Assembly,
                    0.0d);
            }

            public IReadOnlyCollection<string> ReadDefinedTypeNames(string path)
            {
                return ContainsRequestedType
                    ? new[] { "Example.Introduced" }
                    : Array.Empty<string>();
            }

            public void WriteSource(string path, string source)
            {
                WriteSourceCalls++;
            }
        }
    }
}
