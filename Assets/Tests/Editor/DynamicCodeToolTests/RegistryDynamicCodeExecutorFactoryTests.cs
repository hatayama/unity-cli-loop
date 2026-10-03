using System;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies which executor the registry factory hands out depending on whether a compiler is available.
    /// </summary>
    public sealed class RegistryDynamicCodeExecutorFactoryTests
    {
        /// <summary>
        /// Verifies a missing compiler yields the stub executor.
        /// </summary>
        [Test]
        public void Create_WhenNoCompilerIsAvailable_ReturnsTheStub()
        {
            RegistryDynamicCodeExecutorFactory factory = CreateFactory(null);

            IDynamicCodeExecutor executor = factory.Create();

            Assert.That(executor, Is.InstanceOf<DynamicCodeExecutorStub>());
        }

        /// <summary>
        /// Verifies an available compiler yields an executor that owns that compiler.
        /// </summary>
        [Test]
        public void Create_WhenACompilerIsAvailable_ReturnsAnExecutorThatOwnsIt()
        {
            DisposableCompiler compiler = new DisposableCompiler();
            RegistryDynamicCodeExecutorFactory factory = CreateFactory(compiler);

            IDynamicCodeExecutor executor = factory.Create();
            executor.Dispose();

            Assert.That(executor, Is.InstanceOf<DynamicCodeExecutor>());
            Assert.That(compiler.DisposeCallCount, Is.EqualTo(1));
        }

        private static RegistryDynamicCodeExecutorFactory CreateFactory(IDynamicCompilationService compiler)
        {
            return new RegistryDynamicCodeExecutorFactory(
                new FixedCompilationServiceFactory(compiler),
                new UnusedSourcePreparationService(),
                new CompiledCommandEntryPointResolver());
        }

        private sealed class FixedCompilationServiceFactory : IDynamicCompilationServiceFactory
        {
            private readonly IDynamicCompilationService _compiler;

            public FixedCompilationServiceFactory(IDynamicCompilationService compiler)
            {
                _compiler = compiler;
            }

            public IDynamicCompilationService Create() => _compiler;
        }

        private sealed class DisposableCompiler : IDynamicCompilationService, IDisposable
        {
            public int DisposeCallCount { get; private set; }

            public Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
            {
                throw new InvalidOperationException("The factory tests never compile.");
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class UnusedSourcePreparationService : IDynamicCodeSourcePreparationService
        {
            public PreparedDynamicCode Prepare(string source, string namespaceName, string className)
            {
                throw new InvalidOperationException("The factory tests never prepare source.");
            }
        }
    }
}
