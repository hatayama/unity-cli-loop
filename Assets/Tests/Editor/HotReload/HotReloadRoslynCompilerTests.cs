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
    /// Covers the compile sequence both hot-reload compile paths share: compiler discovery, source
    /// writing, and the backend call that either compiler backend actually performs.
    /// </summary>
    public class HotReloadRoslynCompilerTests
    {
        /// <summary>
        /// Verifies that an unresolvable external compiler is reported without writing a source or
        /// calling the backend.
        /// </summary>
        [Test]
        public async Task CompileAsync_PathsUnresolved_WritesNothingAndSkipsBackend()
        {
            FakeEnvironment environment = new FakeEnvironment { PathsAvailable = false };
            HotReloadRoslynCompiler compiler = new HotReloadRoslynCompiler(environment);

            HotReloadRoslynCompileOutcome outcome = await compiler.CompileAsync(
                CreateRequest(sourceCount: 1, allowAssemblyBuilderFallback: false),
                CancellationToken.None);

            Assert.That(outcome.PathsResolved, Is.False);
            Assert.That(outcome.BackendResult, Is.Null);
            Assert.That(environment.WrittenSources, Is.Empty);
            Assert.That(environment.CompileCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that every requested source is written, and written before the backend runs.
        /// </summary>
        [Test]
        public async Task CompileAsync_PathsResolved_WritesEverySourceBeforeBackend()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadRoslynCompiler compiler = new HotReloadRoslynCompiler(environment);

            HotReloadRoslynCompileOutcome outcome = await compiler.CompileAsync(
                CreateRequest(sourceCount: 2, allowAssemblyBuilderFallback: false),
                CancellationToken.None);

            Assert.That(outcome.PathsResolved, Is.True);
            Assert.That(outcome.BackendResult, Is.Not.Null);
            Assert.That(environment.WrittenSources, Has.Count.EqualTo(2));
            Assert.That(environment.WrittenSources[0], Is.EqualTo("source0.cs|// source 0"));
            Assert.That(environment.WrittenSources[1], Is.EqualTo("source1.cs|// source 1"));
            Assert.That(environment.WrittenSourceCountAtCompile, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that the caller's fallback decision reaches the environment unchanged.
        /// </summary>
        [Test]
        public async Task CompileAsync_FallbackAllowed_ReachesEnvironment()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadRoslynCompiler compiler = new HotReloadRoslynCompiler(environment);

            await compiler.CompileAsync(
                CreateRequest(sourceCount: 1, allowAssemblyBuilderFallback: true),
                CancellationToken.None);

            Assert.That(environment.ObservedFallbackAllowed, Is.True);
        }

        /// <summary>
        /// Verifies that a multi-source request cannot ask for the single-source-only fallback.
        /// </summary>
        [Test]
        public void CompileRequest_FallbackWithSeveralSources_IsRejected()
        {
            Assert.Throws<ArgumentException>(
                () => CreateRequest(sourceCount: 2, allowAssemblyBuilderFallback: true));
        }

        /// <summary>
        /// Verifies that a single-source request that allows the fallback still compiles through
        /// the real Roslyn backend when the Unity installation provides it.
        /// </summary>
        [Test]
        public async Task CompileAsync_ProductionSingleSource_CompilesWithRoslynBackend()
        {
            string workDirectory = CreateWorkDirectory();
            try
            {
                string dllPath = Path.Combine(workDirectory, "SingleSource.dll");
                HotReloadRoslynCompileRequest request = new HotReloadRoslynCompileRequest(
                    new[]
                    {
                        new HotReloadRoslynCompileSource(
                            Path.Combine(workDirectory, "SingleSource.cs"),
                            "namespace RoslynCompilerFixture { public class Single { } }")
                    },
                    dllPath,
                    CreateReferencePaths(),
                    Array.Empty<string>(),
                    allowAssemblyBuilderFallback: true);
                HotReloadRoslynCompiler compiler = new HotReloadRoslynCompiler(
                    new HotReloadRoslynCompilerEnvironment());

                HotReloadRoslynCompileOutcome outcome = await compiler.CompileAsync(
                    request,
                    CancellationToken.None);

                Assert.That(outcome.PathsResolved, Is.True);
                Assert.That(outcome.BackendResult, Is.Not.Null);
                Assert.That(HasErrors(outcome.BackendResult.CompilerMessages), Is.False);
                Assert.That(
                    outcome.BackendResult.BackendKind,
                    Is.Not.EqualTo(DynamicCompilationBackendKind.AssemblyBuilderFallback));
                Assert.That(File.Exists(dllPath), Is.True);
            }
            finally
            {
                DeleteDirectory(workDirectory);
            }
        }

        /// <summary>
        /// Verifies that several sources compile into one assembly through the multi-source entry
        /// point that refuses the fallback.
        /// </summary>
        [Test]
        public async Task CompileAsync_ProductionSeveralSources_ProducesOneAssembly()
        {
            string workDirectory = CreateWorkDirectory();
            try
            {
                string dllPath = Path.Combine(workDirectory, "SeveralSources.dll");
                HotReloadRoslynCompileRequest request = new HotReloadRoslynCompileRequest(
                    new[]
                    {
                        new HotReloadRoslynCompileSource(
                            Path.Combine(workDirectory, "First.cs"),
                            "namespace RoslynCompilerFixture { public class First { } }"),
                        new HotReloadRoslynCompileSource(
                            Path.Combine(workDirectory, "Second.cs"),
                            "namespace RoslynCompilerFixture { public class Second : First { } }")
                    },
                    dllPath,
                    CreateReferencePaths(),
                    Array.Empty<string>(),
                    allowAssemblyBuilderFallback: false);
                HotReloadRoslynCompiler compiler = new HotReloadRoslynCompiler(
                    new HotReloadRoslynCompilerEnvironment());

                HotReloadRoslynCompileOutcome outcome = await compiler.CompileAsync(
                    request,
                    CancellationToken.None);

                Assert.That(outcome.PathsResolved, Is.True);
                Assert.That(outcome.BackendResult, Is.Not.Null);
                Assert.That(HasErrors(outcome.BackendResult.CompilerMessages), Is.False);
                Assert.That(File.Exists(dllPath), Is.True);
            }
            finally
            {
                DeleteDirectory(workDirectory);
            }
        }

        private static HotReloadRoslynCompileRequest CreateRequest(
            int sourceCount,
            bool allowAssemblyBuilderFallback)
        {
            List<HotReloadRoslynCompileSource> sources = new List<HotReloadRoslynCompileSource>();
            for (int index = 0; index < sourceCount; index++)
            {
                sources.Add(
                    new HotReloadRoslynCompileSource("source" + index + ".cs", "// source " + index));
            }

            return new HotReloadRoslynCompileRequest(
                sources,
                "artifact.dll",
                Array.Empty<string>(),
                Array.Empty<string>(),
                allowAssemblyBuilderFallback);
        }

        private static bool HasErrors(CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return false;
            }

            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreateWorkDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workDirectory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "RoslynCompilerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            return workDirectory;
        }

        private static void DeleteDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException exception)
            {
                // A leftover work directory must not turn into a test failure of its own.
                UnityEngine.Debug.Log("Work directory could not be removed: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                UnityEngine.Debug.Log("Work directory could not be removed: " + exception.Message);
            }
        }

        private static string[] CreateReferencePaths()
        {
            UnityEditor.Compilation.Assembly targetAssembly = null;
            string testAssemblyName = typeof(HotReloadRoslynCompilerTests).Assembly.GetName().Name;
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

            return references.ToArray();
        }

        private sealed class FakeEnvironment : IHotReloadRoslynCompilerEnvironment
        {
            public bool PathsAvailable { get; set; } = true;

            public List<string> WrittenSources { get; } = new List<string>();

            public int CompileCalls { get; private set; }

            public int WrittenSourceCountAtCompile { get; private set; }

            public bool ObservedFallbackAllowed { get; private set; }

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
                HotReloadRoslynCompileRequest request,
                ExternalCompilerPaths paths,
                CancellationToken ct)
            {
                CompileCalls++;
                WrittenSourceCountAtCompile = WrittenSources.Count;
                ObservedFallbackAllowed = request.AllowAssemblyBuilderFallback;
                DynamicCompilationBackendResult result = new DynamicCompilationBackendResult(
                    Array.Empty<CompilerMessage>(),
                    DynamicCompilationBackendKind.SharedRoslynWorker);
                return Task.FromResult(result);
            }

            // The compiler itself never reads back what it produced; the callers do, through their
            // own use of the same environment.
            public bool FileExists(string path)
            {
                throw new NotSupportedException();
            }

            public AssemblyName ReadAssemblyName(string path)
            {
                throw new NotSupportedException();
            }

            public byte[] ReadAllBytes(string path)
            {
                throw new NotSupportedException();
            }

            public CompiledAssemblyLoadResult Load(byte[] assemblyBytes, byte[] pdbBytes)
            {
                throw new NotSupportedException();
            }

            public IReadOnlyCollection<string> ReadDefinedTypeNames(string path)
            {
                throw new NotSupportedException();
            }

            public void WriteSource(string path, string source)
            {
                WrittenSources.Add(path + "|" + source);
            }
        }
    }
}
