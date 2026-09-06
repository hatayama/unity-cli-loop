using System;
using System.IO;
using System.Linq;

using Mono.Cecil;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Invalidation and capacity contract of the compiled call-site cache. Each test works on
    /// copies of this test assembly's dll in a private temp directory so that mutating the file
    /// never touches ScriptAssemblies.
    /// </summary>
    public class HotReloadCompiledCallSiteCacheTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        // Why this assembly: it is always compiled alongside the test assembly and is smaller, so
        // it can be zero-padded to the test assembly's length for the module identity test.
        private const string OtherAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private string _tempDirectory;
        private HotReloadCompiledCallSiteCache _cache;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-call-site-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _cache = new HotReloadCompiledCallSiteCache(2);
        }

        [TearDown]
        public void TearDown()
        {
            _cache.Clear();
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: an unchanged dll is read once; the second lookup returns the same entry without a reload.
        /// </summary>
        [Test]
        public void GetOrLoad_IdenticalFile_ReusesEntry()
        {
            string dllPath = CopyTestAssembly("a.dll");

            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);
            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(dllPath);

            Assert.That(second, Is.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(1));
            Assert.That(first.CallSites.Count, Is.GreaterThan(0), "A test assembly must contain call sites.");
        }

        /// <summary>
        /// What: a newer write time on a byte-identical file invalidates the entry.
        /// </summary>
        [Test]
        public void GetOrLoad_WriteTimeChanged_Reloads()
        {
            string dllPath = CopyTestAssembly("a.dll");
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);

            File.SetLastWriteTimeUtc(dllPath, File.GetLastWriteTimeUtc(dllPath).AddSeconds(5));
            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(dllPath);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a size change invalidates the entry even when the write time is restored.
        /// </summary>
        [Test]
        public void GetOrLoad_SizeChanged_Reloads()
        {
            string dllPath = CopyTestAssembly("a.dll");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(dllPath);
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);

            // Why append: trailing bytes after the PE image keep the dll readable for Cecil while
            // changing the length, so the test isolates the size check.
            using (FileStream stream = new FileStream(dllPath, FileMode.Append))
            {
                stream.WriteByte(0);
            }

            File.SetLastWriteTimeUtc(dllPath, originalWriteTime);
            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(dllPath);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a same-size rewrite with a restored write time still invalidates the entry when
        /// the module version id differs. Length and mtime alone would miss this case.
        /// </summary>
        [Test]
        public void GetOrLoad_SameSizeSameWriteTimeDifferentModule_Reloads()
        {
            string dllPath = CopyTestAssembly("a.dll");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(dllPath);
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);

            // Why pad: trailing bytes after the PE image keep a dll loadable, so padding the other
            // assembly to the same length isolates the module identity check from the size check.
            byte[] otherModule = ReadOtherAssemblyPaddedTo(new FileInfo(dllPath).Length);
            File.WriteAllBytes(dllPath, otherModule);
            File.SetLastWriteTimeUtc(dllPath, originalWriteTime);

            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(dllPath);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Module.Mvid, Is.Not.EqualTo(first.Module.Mvid));
            Assert.That(_cache.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a byte-identical copy written again with the original write time is reused, so a
        /// rewrite that changes nothing observable does not cost a Cecil read.
        /// </summary>
        [Test]
        public void GetOrLoad_RewrittenIdenticalBytesSameWriteTime_ReusesEntry()
        {
            string dllPath = CopyTestAssembly("a.dll");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(dllPath);
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);

            File.WriteAllBytes(dllPath, File.ReadAllBytes(dllPath));
            File.SetLastWriteTimeUtc(dllPath, originalWriteTime);

            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(dllPath);

            Assert.That(second, Is.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: when the dll is replaced between the fingerprint and the Cecil read, the entry
        /// published describes the file actually read (fingerprint MVID equals the module MVID),
        /// never the pre-replacement fingerprint paired with the post-replacement view.
        /// </summary>
        [Test]
        public void GetOrLoad_FileReplacedBetweenFingerprintAndRead_PublishesConsistentEntry()
        {
            string dllPath = CopyTestAssembly("a.dll");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(dllPath);
            byte[] otherModule = ReadOtherAssemblyPaddedTo(new FileInfo(dllPath).Length);
            int readCount = 0;
            HotReloadCompiledCallSiteCache.LoadProbes probes = new HotReloadCompiledCallSiteCache.LoadProbes
            {
                BeforeAssemblyRead = _ =>
                {
                    readCount++;
                    if (readCount != 1)
                    {
                        return;
                    }

                    File.WriteAllBytes(dllPath, otherModule);
                    File.SetLastWriteTimeUtc(dllPath, originalWriteTime);
                }
            };
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(2, probes);
            try
            {
                HotReloadCompiledCallSiteCache.Entry entry = cache.GetOrLoad(dllPath);

                Guid onDisk = ModuleDefinition.ReadModule(dllPath, new ReaderParameters { ReadingMode = ReadingMode.Deferred }).Mvid;
                Assert.That(entry.Fingerprint.ModuleVersionId, Is.EqualTo(entry.Module.Mvid));
                Assert.That(entry.Module.Mvid, Is.EqualTo(onDisk));
                Assert.That(cache.LoadCount, Is.EqualTo(2), "The inconsistent first read must be discarded and retried.");
                Assert.That(cache.Count, Is.EqualTo(1));
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: a dll that changes before every read is reported as an I/O failure instead of a
        /// stale entry, and nothing is published.
        /// </summary>
        [Test]
        public void GetOrLoad_FileChangesBeforeEveryRead_ThrowsWithoutPublishing()
        {
            string dllPath = CopyTestAssembly("a.dll");
            byte[] original = File.ReadAllBytes(dllPath);
            byte[] otherModule = ReadOtherAssemblyPaddedTo(original.Length);
            bool useOther = true;
            HotReloadCompiledCallSiteCache.LoadProbes probes = new HotReloadCompiledCallSiteCache.LoadProbes
            {
                BeforeAssemblyRead = _ =>
                {
                    File.WriteAllBytes(dllPath, useOther ? otherModule : original);
                    useOther = !useOther;
                }
            };
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(2, probes);
            try
            {
                Assert.Throws<IOException>(() => cache.GetOrLoad(dllPath));
                Assert.That(cache.Count, Is.EqualTo(0));
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: a failure while the index is being built releases the Cecil assembly and lets the
        /// original exception through unchanged; the cache stays empty.
        /// </summary>
        [Test]
        public void GetOrLoad_IndexBuildFails_DisposesAssemblyAndRethrows()
        {
            string dllPath = CopyTestAssembly("a.dll");
            InvalidOperationException injected = new InvalidOperationException("index failure for test");
            AssemblyDefinition captured = null;
            HotReloadCompiledCallSiteCache.LoadProbes probes = new HotReloadCompiledCallSiteCache.LoadProbes
            {
                AfterAssemblyRead = assembly =>
                {
                    captured = assembly;
                    throw injected;
                }
            };
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(2, probes);
            try
            {
                InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => cache.GetOrLoad(dllPath));

                Assert.That(thrown, Is.SameAs(injected));
                Assert.That(captured, Is.Not.Null);
                // Why this observation: metadata tables are already in memory, but a method body
                // is read lazily from the image stream, which a disposed module has closed. That
                // read is the only externally visible trace of AssemblyDefinition.Dispose().
                Assert.Throws<ObjectDisposedException>(() => ReadFirstMethodBody(captured));
                Assert.That(cache.Count, Is.EqualTo(0));
                Assert.That(cache.LoadCount, Is.EqualTo(0));
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: the same bytes under a different path are a separate entry and trigger a load.
        /// </summary>
        [Test]
        public void GetOrLoad_DifferentPath_LoadsSeparately()
        {
            string firstPath = CopyTestAssembly("a.dll");
            string secondPath = CopyTestAssembly("b.dll");

            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(firstPath);
            HotReloadCompiledCallSiteCache.Entry second = _cache.GetOrLoad(secondPath);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(2));
            Assert.That(_cache.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: loading beyond the capacity evicts the least recently used entry, so the count
        /// never exceeds the cap and the evicted dll is read again on its next lookup.
        /// </summary>
        [Test]
        public void GetOrLoad_OverCapacity_EvictsLeastRecentlyUsed()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");

            _cache.GetOrLoad(pathA);
            _cache.GetOrLoad(pathB);
            _cache.GetOrLoad(pathA);
            _cache.GetOrLoad(pathC);
            Assert.That(_cache.Count, Is.EqualTo(2));
            Assert.That(_cache.LoadCount, Is.EqualTo(3));

            _cache.GetOrLoad(pathA);
            Assert.That(_cache.LoadCount, Is.EqualTo(3), "A stayed cached because it was used more recently than B.");

            _cache.GetOrLoad(pathB);
            Assert.That(_cache.LoadCount, Is.EqualTo(4), "B was the least recently used entry and had to be reloaded.");
            Assert.That(_cache.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a non-positive capacity is rejected, because the cache could never hold an entry.
        /// </summary>
        [Test]
        public void Constructor_NonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HotReloadCompiledCallSiteCache(0));
        }

        private static void ReadFirstMethodBody(AssemblyDefinition assembly)
        {
            MethodDefinition firstWithBody = assembly.MainModule.GetTypes()
                .SelectMany(type => type.Methods)
                .First(method => method.HasBody);
            _ = firstWithBody.Body.Instructions.Count;
        }

        private static byte[] ReadOtherAssemblyPaddedTo(long length)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string otherDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                OtherAssemblyName + ".dll");
            Assert.That(File.Exists(otherDllPath), Is.True, "Other assembly dll missing: " + otherDllPath);
            byte[] bytes = File.ReadAllBytes(otherDllPath);
            Assert.That(bytes.Length, Is.LessThanOrEqualTo(length), "The other assembly must not be larger than the padded target.");

            byte[] padded = new byte[length];
            Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
            return padded;
        }

        private string CopyTestAssembly(string fileName)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(sourceDllPath), Is.True, "Test assembly dll missing: " + sourceDllPath);

            string destinationPath = Path.Combine(_tempDirectory, fileName);
            File.Copy(sourceDllPath, destinationPath);
            return destinationPath;
        }
    }
}
