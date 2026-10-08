using System;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies the shared Roslyn worker assembly cache: its key, and copying
    /// assemblies out of and publishing them into a cache directory.
    /// </summary>
    [TestFixture]
    public class SharedRoslynCompilerWorkerAssemblyCacheTests
    {
        private static readonly byte[] AssemblyBytes = { 0x4D, 0x5A, 0x01, 0x02 };

        private string _rootPath;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(
                Path.GetTempPath(),
                "RoslynWorkerAssemblyCacheTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, true);
            }
        }

        /// <summary>
        /// Verifies that the key is a stable 64-character lowercase hex string for the same inputs.
        /// </summary>
        [Test]
        public void ComputeCacheKey_SameSourceAndPaths_ReturnsTheSameLowercaseHexKey()
        {
            string first = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source", CreatePaths("csc.dll"));
            string second = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source", CreatePaths("csc.dll"));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Match("^[0-9a-f]{64}$"));
        }

        /// <summary>
        /// Verifies that a one-character change in the worker source changes the key.
        /// </summary>
        [Test]
        public void ComputeCacheKey_SourceDiffersByOneCharacter_ReturnsADifferentKey()
        {
            string first = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source A", CreatePaths("csc.dll"));
            string second = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source B", CreatePaths("csc.dll"));

            Assert.That(first, Is.Not.EqualTo(second));
        }

        /// <summary>
        /// Verifies that another compiler path changes the key, so two Unity versions do not share one assembly.
        /// </summary>
        [Test]
        public void ComputeCacheKey_CompilerDllPathDiffers_ReturnsADifferentKey()
        {
            string first = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source", CreatePaths("first/csc.dll"));
            string second = SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(
                "// source", CreatePaths("second/csc.dll"));

            Assert.That(first, Is.Not.EqualTo(second));
        }

        /// <summary>
        /// Verifies that a missing cached assembly is reported as not copied.
        /// </summary>
        [Test]
        public void TryCopyCachedAssembly_WhenTheCachedAssemblyIsMissing_ReturnsFalse()
        {
            string destination = Path.Combine(_rootPath, "worker.dll");

            bool copied = SharedRoslynCompilerWorkerAssemblyCache.TryCopyCachedAssembly(
                Path.Combine(_rootPath, "missing.dll"), destination);

            Assert.That(copied, Is.False);
            Assert.That(File.Exists(destination), Is.False);
        }

        /// <summary>
        /// Verifies that an empty cached file, which a crashed Editor can leave, counts as no cached assembly.
        /// </summary>
        [Test]
        public void TryCopyCachedAssembly_WhenTheCachedAssemblyIsEmpty_ReturnsFalse()
        {
            string cached = Path.Combine(_rootPath, "cached.dll");
            File.WriteAllBytes(cached, Array.Empty<byte>());
            string destination = Path.Combine(_rootPath, "worker.dll");

            bool copied = SharedRoslynCompilerWorkerAssemblyCache.TryCopyCachedAssembly(cached, destination);

            Assert.That(copied, Is.False);
            Assert.That(File.Exists(destination), Is.False);
        }

        /// <summary>
        /// Verifies that a cached assembly is copied to the worker directory.
        /// </summary>
        [Test]
        public void TryCopyCachedAssembly_WhenTheCachedAssemblyExists_CopiesItAndReturnsTrue()
        {
            string cached = Path.Combine(_rootPath, "cached.dll");
            File.WriteAllBytes(cached, AssemblyBytes);
            string destination = Path.Combine(_rootPath, "worker.dll");

            bool copied = SharedRoslynCompilerWorkerAssemblyCache.TryCopyCachedAssembly(cached, destination);

            Assert.That(copied, Is.True);
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(AssemblyBytes));
        }

        /// <summary>
        /// Verifies that a copy failing for an environmental reason returns false instead of throwing,
        /// so the caller builds the assembly instead.
        /// </summary>
        [Test]
        public void TryCopyCachedAssembly_WhenTheDestinationDirectoryIsMissing_ReturnsFalseWithoutThrowing()
        {
            string cached = Path.Combine(_rootPath, "cached.dll");
            File.WriteAllBytes(cached, AssemblyBytes);
            string destination = Path.Combine(_rootPath, "missing", "worker.dll");

            bool copied = true;
            Assert.DoesNotThrow(() =>
                copied = SharedRoslynCompilerWorkerAssemblyCache.TryCopyCachedAssembly(cached, destination));

            Assert.That(copied, Is.False);
            Assert.That(File.Exists(destination), Is.False);
        }

        /// <summary>
        /// Verifies that a built assembly is published to an empty cache and no temp file remains.
        /// </summary>
        [Test]
        public void PublishBuiltAssembly_WhenTheCacheIsEmpty_PublishesTheAssemblyWithoutLeavingATempFile()
        {
            string built = WriteBuiltAssembly();
            string cached = Path.Combine(_rootPath, "cache", "key", "RoslynCompilerWorker.dll");

            CachePublishOutcome outcome = SharedRoslynCompilerWorkerAssemblyCache.PublishBuiltAssembly(built, cached);

            Assert.That(outcome.Kind, Is.EqualTo(CachePublishKind.Published));
            Assert.That(outcome.Error, Is.Empty);
            Assert.That(File.ReadAllBytes(cached), Is.EqualTo(AssemblyBytes));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(cached)), Is.EqualTo(new[] { cached }));
        }

        /// <summary>
        /// Verifies that an assembly another Editor already published is left untouched.
        /// </summary>
        [Test]
        public void PublishBuiltAssembly_WhenTheCachedAssemblyExists_LeavesItUntouched()
        {
            string built = WriteBuiltAssembly();
            string cached = Path.Combine(_rootPath, "cache", "key", "RoslynCompilerWorker.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            byte[] publishedBytes = { 0x4D, 0x5A, 0x09 };
            File.WriteAllBytes(cached, publishedBytes);
            DateTime fixedWriteTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(cached, fixedWriteTime);

            CachePublishOutcome outcome = SharedRoslynCompilerWorkerAssemblyCache.PublishBuiltAssembly(built, cached);

            Assert.That(outcome.Kind, Is.EqualTo(CachePublishKind.AlreadyPresent));
            Assert.That(File.GetLastWriteTimeUtc(cached), Is.EqualTo(fixedWriteTime));
            Assert.That(File.ReadAllBytes(cached), Is.EqualTo(publishedBytes));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(cached)), Is.EqualTo(new[] { cached }));
        }

        /// <summary>
        /// Verifies that an empty cached file is replaced, so it does not stay in the cache forever.
        /// </summary>
        [Test]
        public void PublishBuiltAssembly_WhenTheCachedAssemblyIsEmpty_ReplacesIt()
        {
            string built = WriteBuiltAssembly();
            string cached = Path.Combine(_rootPath, "cache", "key", "RoslynCompilerWorker.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            File.WriteAllBytes(cached, Array.Empty<byte>());

            CachePublishOutcome outcome = SharedRoslynCompilerWorkerAssemblyCache.PublishBuiltAssembly(built, cached);

            Assert.That(outcome.Kind, Is.EqualTo(CachePublishKind.Published));
            Assert.That(File.ReadAllBytes(cached), Is.EqualTo(AssemblyBytes));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(cached)), Is.EqualTo(new[] { cached }));
        }

        /// <summary>
        /// Verifies that a cache directory that cannot be created yields a failed outcome with an error
        /// instead of an exception, and leaves no temp file behind.
        /// </summary>
        [Test]
        public void PublishBuiltAssembly_WhenTheCacheDirectoryCannotBeCreated_ReturnsFailedWithoutThrowing()
        {
            string built = WriteBuiltAssembly();
            string blockingFile = Path.Combine(_rootPath, "cache");
            File.WriteAllBytes(blockingFile, AssemblyBytes);
            string cached = Path.Combine(blockingFile, "key", "RoslynCompilerWorker.dll");

            CachePublishOutcome outcome = default;
            Assert.DoesNotThrow(() =>
                outcome = SharedRoslynCompilerWorkerAssemblyCache.PublishBuiltAssembly(built, cached));

            Assert.That(outcome.Kind, Is.EqualTo(CachePublishKind.Failed));
            Assert.That(outcome.Error, Is.Not.Empty);
            Assert.That(
                Directory.GetFiles(_rootPath),
                Is.EquivalentTo(new[] { built, blockingFile }));
        }

        /// <summary>
        /// Verifies that the default outcome means nothing was offered to the cache.
        /// </summary>
        [Test]
        public void CachePublishOutcome_Default_IsNotAttempted()
        {
            CachePublishOutcome outcome = default;

            Assert.That(outcome.Kind, Is.EqualTo(CachePublishKind.NotAttempted));
            Assert.That(outcome.Error, Is.Empty);
            Assert.That(outcome.ToVibeValue(), Is.EqualTo("not_attempted"));
        }

        /// <summary>
        /// Verifies the vibe value written for each publish outcome.
        /// </summary>
        [Test]
        public void CachePublishOutcome_ToVibeValue_NamesEachOutcome()
        {
            Assert.That(CachePublishOutcome.NotAttempted.ToVibeValue(), Is.EqualTo("not_attempted"));
            Assert.That(CachePublishOutcome.Published.ToVibeValue(), Is.EqualTo("published"));
            Assert.That(CachePublishOutcome.AlreadyPresent.ToVibeValue(), Is.EqualTo("present"));
            Assert.That(CachePublishOutcome.Failed("IOException: denied").ToVibeValue(), Is.EqualTo("failed"));
            Assert.That(CachePublishOutcome.Failed("IOException: denied").Error, Is.EqualTo("IOException: denied"));
        }

        private string WriteBuiltAssembly()
        {
            string built = Path.Combine(_rootPath, "built.dll");
            File.WriteAllBytes(built, AssemblyBytes);
            return built;
        }

        private static ExternalCompilerPaths CreatePaths(string compilerDllPath)
        {
            return new ExternalCompilerPaths(
                "contents",
                "scripting",
                "dotnet",
                compilerDllPath,
                "csc.runtimeconfig.json",
                "csc.deps.json",
                "Microsoft.CodeAnalysis.dll",
                null,
                "shared",
                ExternalCompilerLayoutKind.Unknown);
        }
    }
}
