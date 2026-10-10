using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Mono.Cecil;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Invalidation, byte budget, run hold, and call-site index contract of the compiled call-site
    /// cache. Each test works on copies of this test assembly's dll in a private temp directory so
    /// that mutating the file never touches ScriptAssemblies.
    /// </summary>
    public class HotReloadCompiledCallSiteCacheTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string ScannerFixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerFixture";
        private const string NestedCallerHostTypeMetadataName = ScannerFixtureTypeMetadataName + "/NestedCallerHost";
        private const string GenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GenericHost`1";
        // Why this assembly: it is always compiled alongside the test assembly and is smaller, so
        // it can be zero-padded to the test assembly's length for the module identity test.
        private const string OtherAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";
        // Why a write time the test chooses: SetLastWriteTimeUtc stores microseconds while
        // GetLastWriteTimeUtc reports 100 ns ticks, so a write time read back from a file cannot be
        // restored exactly unless it happens to fall on a microsecond. Tests that restore the write
        // time to isolate another field would otherwise leave it shifted by a few ticks.
        private static readonly DateTime PinnedWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private string _tempDirectory;
        private HotReloadCompiledCallSiteCache _cache;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-call-site-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            _cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(2));
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
        /// What: with loads allowed, a stale entry is replaced and the lookup reports that it read
        /// the file.
        /// </summary>
        [Test]
        public void TryGetOrLoad_StaleEntryWithLoadAllowed_ReplacesItAndReportsLoaded()
        {
            string dllPath = CopyTestAssembly("a.dll");
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);
            File.SetLastWriteTimeUtc(dllPath, File.GetLastWriteTimeUtc(dllPath).AddSeconds(5));

            bool available = _cache.TryGetOrLoad(dllPath, true, out HotReloadCompiledCallSiteCache.Entry second, out bool loaded);

            Assert.That(available, Is.True);
            Assert.That(loaded, Is.True);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(_cache.LoadCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: with loads refused, a stale entry is reported as not available and kept, so the
        /// next allowed lookup still sees it as stale and reads the file.
        /// </summary>
        [Test]
        public void TryGetOrLoad_StaleEntryWithoutLoadAllowed_ReturnsFalseAndKeepsTheOldEntry()
        {
            string dllPath = CopyTestAssembly("a.dll");
            HotReloadCompiledCallSiteCache.Entry first = _cache.GetOrLoad(dllPath);
            File.SetLastWriteTimeUtc(dllPath, File.GetLastWriteTimeUtc(dllPath).AddSeconds(5));

            bool available = _cache.TryGetOrLoad(dllPath, false, out HotReloadCompiledCallSiteCache.Entry refused, out bool loaded);

            Assert.That(available, Is.False);
            Assert.That(loaded, Is.False);
            Assert.That(refused, Is.Null);
            Assert.That(_cache.LoadCount, Is.EqualTo(1));
            Assert.That(_cache.Count, Is.EqualTo(1));

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
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(2), probes);
            try
            {
                HotReloadCompiledCallSiteCache.Entry entry = cache.GetOrLoad(dllPath);

                Guid onDisk;
                // Why dispose: an undisposed module keeps the dll open, and the TearDown that
                // deletes the temp directory then fails on Windows.
                using (ModuleDefinition module = ModuleDefinition.ReadModule(dllPath, new ReaderParameters { ReadingMode = ReadingMode.Deferred }))
                {
                    onDisk = module.Mvid;
                }
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
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(2), probes);
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
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(2), probes);
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
        /// What: loading beyond the budget evicts the least recently used entry, so the cached bytes
        /// never exceed the budget and the evicted dll is read again on its next lookup.
        /// </summary>
        [Test]
        public void GetOrLoad_OverBudget_EvictsLeastRecentlyUsed()
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
        /// What: a non-positive budget is rejected, because the cache could never hold an entry.
        /// </summary>
        [Test]
        public void Constructor_NonPositiveBudget_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HotReloadCompiledCallSiteCache(0));
        }

        /// <summary>
        /// What: inside a hold each dll is read once even when a run touches more bytes than the
        /// budget, so a run never re-reads a dll it already read.
        /// </summary>
        [Test]
        public void GetOrLoad_InsideHold_ReadsEachDllOnce_EvenBeyondBudget()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");

            using (_cache.HoldEntriesForRun())
            {
                HotReloadCompiledCallSiteCache.Entry firstA = _cache.GetOrLoad(pathA);
                HotReloadCompiledCallSiteCache.Entry firstB = _cache.GetOrLoad(pathB);
                HotReloadCompiledCallSiteCache.Entry firstC = _cache.GetOrLoad(pathC);
                HotReloadCompiledCallSiteCache.Entry secondA = _cache.GetOrLoad(pathA);
                HotReloadCompiledCallSiteCache.Entry secondB = _cache.GetOrLoad(pathB);
                HotReloadCompiledCallSiteCache.Entry secondC = _cache.GetOrLoad(pathC);

                Assert.That(_cache.LoadCount, Is.EqualTo(3));
                Assert.That(_cache.Count, Is.EqualTo(3));
                Assert.That(secondA, Is.SameAs(firstA));
                Assert.That(secondB, Is.SameAs(firstB));
                Assert.That(secondC, Is.SameAs(firstC));
            }
        }

        /// <summary>
        /// What: ending the hold evicts the least recently used entries down to the budget, so
        /// the dlls used last stay cached for the next run and the evicted one is read again.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_Dispose_EvictsDownToBudget()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");
            using (_cache.HoldEntriesForRun())
            {
                _cache.GetOrLoad(pathA);
                _cache.GetOrLoad(pathB);
                _cache.GetOrLoad(pathC);
                _cache.GetOrLoad(pathA);
                _cache.GetOrLoad(pathB);
                _cache.GetOrLoad(pathC);
            }

            Assert.That(_cache.Count, Is.EqualTo(2));
            // Why B and C before A: outside a hold a miss evicts one entry before it adds, so
            // reading A first would evict B and hide whether B survived the end of the hold.
            _cache.GetOrLoad(pathB);
            _cache.GetOrLoad(pathC);
            Assert.That(_cache.LoadCount, Is.EqualTo(3), "B and C were used last and stayed cached.");
            _cache.GetOrLoad(pathA);
            Assert.That(_cache.LoadCount, Is.EqualTo(4), "A was used least recently and was evicted when the hold ended.");
        }

        /// <summary>
        /// What: ending an inner hold keeps every entry while the outer hold is open; only the end
        /// of the outer hold evicts down to the budget.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_Nested_KeepsEntriesUntilTheOuterHoldEnds()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");

            using (_cache.HoldEntriesForRun())
            {
                using (_cache.HoldEntriesForRun())
                {
                    _cache.GetOrLoad(pathA);
                    _cache.GetOrLoad(pathB);
                    _cache.GetOrLoad(pathC);
                }

                Assert.That(_cache.Count, Is.EqualTo(3), "The outer hold is still open.");
            }

            Assert.That(_cache.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: disposing a hold twice releases it once, so a repeated Dispose cannot end the
        /// outer hold early and let a run's entries be evicted.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_DisposeTwice_IsIgnored()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");

            using (_cache.HoldEntriesForRun())
            {
                IDisposable inner = _cache.HoldEntriesForRun();
                inner.Dispose();
                inner.Dispose();

                _cache.GetOrLoad(pathA);
                _cache.GetOrLoad(pathB);
                _cache.GetOrLoad(pathC);
                Assert.That(_cache.Count, Is.EqualTo(3), "The outer hold is still open.");
            }

            Assert.That(_cache.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: without a hold a miss still evicts the least recently used entry before it adds,
        /// so lookups outside a run keep the cache within its budget.
        /// </summary>
        [Test]
        public void GetOrLoad_WithoutHold_StillEvictsBeforeAdding()
        {
            _cache.GetOrLoad(CopyTestAssembly("a.dll"));
            _cache.GetOrLoad(CopyTestAssembly("b.dll"));
            _cache.GetOrLoad(CopyTestAssembly("c.dll"));

            Assert.That(_cache.Count, Is.EqualTo(2));
            Assert.That(_cache.LoadCount, Is.EqualTo(3));
            Assert.That(_cache.CachedBytes, Is.EqualTo(BudgetForEntries(2)));
        }

        /// <summary>
        /// What: when every dll a run touched fits the budget, ending the hold evicts nothing, so
        /// the next run reads none of them again. A count cap smaller than the run evicted some.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_Dispose_KeepsEveryEntryWithinBudget()
        {
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(3));
            try
            {
                string pathA = CopyTestAssembly("a.dll");
                string pathB = CopyTestAssembly("b.dll");
                string pathC = CopyTestAssembly("c.dll");
                using (cache.HoldEntriesForRun())
                {
                    cache.GetOrLoad(pathA);
                    cache.GetOrLoad(pathB);
                    cache.GetOrLoad(pathC);
                }

                Assert.That(cache.Count, Is.EqualTo(3));
                Assert.That(cache.CachedBytes, Is.EqualTo(BudgetForEntries(3)));
                Assert.That(cache.LastHoldReleaseEvictedCount, Is.EqualTo(0));

                cache.GetOrLoad(pathA);
                cache.GetOrLoad(pathB);
                cache.GetOrLoad(pathC);
                Assert.That(cache.LoadCount, Is.EqualTo(3), "The next run reads none of the dlls again.");
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: ending a hold over the budget records how many entries and bytes it evicted.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_Dispose_RecordsWhatItEvicted()
        {
            using (_cache.HoldEntriesForRun())
            {
                _cache.GetOrLoad(CopyTestAssembly("a.dll"));
                _cache.GetOrLoad(CopyTestAssembly("b.dll"));
                _cache.GetOrLoad(CopyTestAssembly("c.dll"));
            }

            Assert.That(_cache.Count, Is.EqualTo(2));
            Assert.That(_cache.CachedBytes, Is.EqualTo(BudgetForEntries(2)));
            Assert.That(_cache.LastHoldReleaseEvictedCount, Is.EqualTo(1));
            Assert.That(_cache.LastHoldReleaseEvictedBytes, Is.EqualTo(TestAssemblyLength()));
        }

        /// <summary>
        /// What: ending a hold keeps the last entry even when it alone exceeds the budget, so the
        /// end of a run never empties the cache.
        /// </summary>
        [Test]
        public void HoldEntriesForRun_Dispose_KeepsTheLastEntryOverBudget()
        {
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(TestAssemblyLength() / 2);
            try
            {
                using (cache.HoldEntriesForRun())
                {
                    cache.GetOrLoad(CopyTestAssembly("a.dll"));
                    cache.GetOrLoad(CopyTestAssembly("b.dll"));
                }

                Assert.That(cache.Count, Is.EqualTo(1));
                Assert.That(cache.CachedBytes, Is.EqualTo(TestAssemblyLength()));
                Assert.That(cache.LastHoldReleaseEvictedCount, Is.EqualTo(1));
                Assert.That(cache.LastHoldReleaseEvictedBytes, Is.EqualTo(TestAssemblyLength()));
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: a dll larger than the whole budget is still cached, alone, and the next dll
        /// replaces it, so a large assembly never makes a lookup fail or loop.
        /// </summary>
        [Test]
        public void GetOrLoad_EntryLargerThanBudget_IsStillLoaded()
        {
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(TestAssemblyLength() / 2);
            try
            {
                cache.GetOrLoad(CopyTestAssembly("a.dll"));
                Assert.That(cache.Count, Is.EqualTo(1));
                Assert.That(cache.CachedBytes, Is.EqualTo(TestAssemblyLength()));

                cache.GetOrLoad(CopyTestAssembly("b.dll"));
                Assert.That(cache.Count, Is.EqualTo(1), "A was evicted to make room for B.");
                Assert.That(cache.LoadCount, Is.EqualTo(2));
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: replacing a stale entry counts the new file's length instead of adding it to the
        /// old one, so the cached bytes match what is actually held.
        /// </summary>
        [Test]
        public void GetOrLoad_ReplacedEntry_UpdatesCachedBytes()
        {
            string dllPath = CopyTestAssembly("a.dll");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(dllPath);
            _cache.GetOrLoad(dllPath);

            // Why append: trailing bytes after the PE image keep the dll readable for Cecil while
            // changing the length, as in GetOrLoad_SizeChanged_Reloads.
            using (FileStream stream = new FileStream(dllPath, FileMode.Append))
            {
                stream.WriteByte(0);
            }

            File.SetLastWriteTimeUtc(dllPath, originalWriteTime);
            _cache.GetOrLoad(dllPath);

            Assert.That(_cache.CachedBytes, Is.EqualTo(new FileInfo(dllPath).Length));
            Assert.That(_cache.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: inside a hold a dll whose fingerprint changed is read again in place without
        /// evicting another entry, and the other entries are still served without a read.
        /// </summary>
        [Test]
        public void GetOrLoad_InsideHold_StaleFingerprint_ReloadsWithoutEvicting()
        {
            string pathA = CopyTestAssembly("a.dll");
            string pathB = CopyTestAssembly("b.dll");
            string pathC = CopyTestAssembly("c.dll");

            using (_cache.HoldEntriesForRun())
            {
                _cache.GetOrLoad(pathA);
                _cache.GetOrLoad(pathB);
                _cache.GetOrLoad(pathC);

                File.SetLastWriteTimeUtc(pathA, File.GetLastWriteTimeUtc(pathA).AddSeconds(5));
                _cache.GetOrLoad(pathA);
                Assert.That(_cache.LoadCount, Is.EqualTo(4));
                Assert.That(_cache.Count, Is.EqualTo(3));

                _cache.GetOrLoad(pathB);
                _cache.GetOrLoad(pathC);
                Assert.That(_cache.LoadCount, Is.EqualTo(4), "Reading A again must not evict B or C.");
            }
        }

        /// <summary>
        /// What: once a dll is indexed, its cached view no longer holds the methods' instruction
        /// lists, so an entry keeps only the dll bytes and the metadata.
        /// </summary>
        [Test]
        public void GetOrLoad_ReleasesMethodBodiesAfterIndexing()
        {
            // Why a private field: Cecil gives no public way to observe a released body. RVA and
            // HasBody do not change, and reading Body reads it again from the image, so this peeks
            // at the field name of the Cecil version the package pins (1.11.6).
            FieldInfo bodyField = typeof(MethodDefinition).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic);
            if (bodyField == null)
            {
                Assert.Fail("MethodDefinition has no private 'body' field: the Cecil version changed, so update this probe.");
            }

            AssemblyDefinition captured = null;
            HotReloadCompiledCallSiteCache.LoadProbes probes = new HotReloadCompiledCallSiteCache.LoadProbes
            {
                AfterAssemblyRead = assembly => captured = assembly
            };
            HotReloadCompiledCallSiteCache cache = new HotReloadCompiledCallSiteCache(BudgetForEntries(2), probes);
            try
            {
                HotReloadCompiledCallSiteCache.Entry entry = cache.GetOrLoad(CopyTestAssembly("a.dll"));

                TypeDefinition fixtureType = captured.MainModule.GetType(ScannerFixtureTypeMetadataName);
                MethodDefinition ordinaryCaller = fixtureType.Methods.Single(
                    method => method.Name == nameof(HotReloadCallSiteScannerFixture.OrdinaryCaller));
                // Why check the walk first: Cecil reads a body only when it is asked for, so a body
                // that was never walked is also null and would pass for the wrong reason.
                Assert.That(
                    entry.CallSites.Any(callSite => callSite.Caller == ordinaryCaller),
                    Is.True,
                    "The caller's body must have been walked for its call sites.");
                Assert.That(bodyField.GetValue(ordinaryCaller), Is.Null);
            }
            finally
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// What: an entry looks up call sites by the open declaring type's full name and the method
        /// name, so a call through GenericHost&lt;int&gt; is found under GenericHost`1, while the
        /// closed type name or another method name finds nothing.
        /// </summary>
        [Test]
        public void GetOrLoad_IndexesCallSitesByOpenDeclaringTypeAndMethodName()
        {
            const string targetMethodName = nameof(GenericHost<int>.Target);
            HotReloadCompiledCallSiteCache.Entry entry = _cache.GetOrLoad(CopyTestAssembly("a.dll"));

            IReadOnlyList<int> indices = entry.LookupCallSiteIndices(GenericHostTypeMetadataName, targetMethodName);

            Assert.That(indices, Is.Not.Empty);
            foreach (int index in indices)
            {
                MethodReference openMethod = entry.CallSites[index].Operand.GetElementMethod();
                Assert.That(openMethod.Name, Is.EqualTo(targetMethodName));
                Assert.That(openMethod.DeclaringType.GetElementType().FullName, Is.EqualTo(GenericHostTypeMetadataName));
            }

            Assert.That(
                entry.LookupCallSiteIndices(GenericHostTypeMetadataName + "<System.Int32>", targetMethodName),
                Is.Empty,
                "The index key is the open type name.");
            Assert.That(
                entry.LookupCallSiteIndices(GenericHostTypeMetadataName, targetMethodName + "Missing"),
                Is.Empty,
                "The index key includes the method name.");
        }

        /// <summary>
        /// What: a call into a nested type is indexed under the type's metadata name, which nests
        /// with '/', so the reflection spelling with '+' finds nothing.
        /// </summary>
        [Test]
        public void GetOrLoad_IndexesCallSitesOfANestedDeclaringTypeUnderTheMetadataName()
        {
            const string targetMethodName = nameof(HotReloadCallSiteScannerFixture.NestedCallerHost.CalledFromOuterType);
            HotReloadCompiledCallSiteCache.Entry entry = _cache.GetOrLoad(CopyTestAssembly("a.dll"));

            IReadOnlyList<int> indices = entry.LookupCallSiteIndices(NestedCallerHostTypeMetadataName, targetMethodName);

            Assert.That(indices, Is.Not.Empty);
            foreach (int index in indices)
            {
                MethodReference operand = entry.CallSites[index].Operand;
                Assert.That(operand.Name, Is.EqualTo(targetMethodName));
                Assert.That(operand.DeclaringType.FullName, Is.EqualTo(NestedCallerHostTypeMetadataName));
            }

            Assert.That(
                entry.LookupCallSiteIndices(ScannerFixtureTypeMetadataName + "+NestedCallerHost", targetMethodName),
                Is.Empty,
                "The index key nests with the metadata separator, not the reflection one.");
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

        private static string TestAssemblySourcePath()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(sourceDllPath), Is.True, "Test assembly dll missing: " + sourceDllPath);
            return sourceDllPath;
        }

        // Every fixture dll is a copy of the test assembly, so all of them have this length.
        private static long TestAssemblyLength()
        {
            return new FileInfo(TestAssemblySourcePath()).Length;
        }

        private static long BudgetForEntries(int count)
        {
            return count * TestAssemblyLength();
        }

        private string CopyTestAssembly(string fileName)
        {
            string sourceDllPath = TestAssemblySourcePath();

            string destinationPath = Path.Combine(_tempDirectory, fileName);
            File.Copy(sourceDllPath, destinationPath);
            File.SetLastWriteTimeUtc(destinationPath, PinnedWriteTimeUtc);
            Assert.That(
                File.GetLastWriteTimeUtc(destinationPath),
                Is.EqualTo(PinnedWriteTimeUtc),
                "The copy's write time must be settable exactly, or restoring it later shifts the fingerprint.");
            return destinationPath;
        }
    }
}
