using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for compiled call-site enumeration and async logical-owner resolution.
    /// </summary>
    public class HotReloadCallSiteScannerTests
    {
        private const string TestScriptProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadCallSiteScannerTests.cs";

        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerFixture";

        /// <summary>
        /// What: a method called from an ordinary method is reported with that caller's method key.
        /// </summary>
        [Test]
        public void FindCallSites_OrdinaryCaller_ReportsCallerMethodKey()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                nameof(HotReloadCallSiteScannerFixture.CalledFromOrdinaryMethod));

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::OrdinaryCaller()"));
            Assert.That(
                hits[0].CallerMethodName,
                Is.EqualTo(nameof(HotReloadCallSiteScannerFixture.OrdinaryCaller)));
        }

        /// <summary>
        /// What: a method with no compiled call sites yields zero hits.
        /// </summary>
        [Test]
        public void FindCallSites_NeverCalled_ReturnsEmpty()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                nameof(HotReloadCallSiteScannerFixture.NeverCalled));

            Assert.That(hits, Is.Empty);
        }

        /// <summary>
        /// What: a method referenced only by delegate assignment is found via Ldftn.
        /// </summary>
        [Test]
        public void FindCallSites_DelegateAssignment_ReportsLdftnCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                nameof(HotReloadCallSiteScannerFixture.CalledOnlyViaDelegate));

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::CaptureDelegate()"));
        }

        /// <summary>
        /// What: a call from an async method is reported under the logical owner method key,
        /// not the compiler-generated MoveNext.
        /// </summary>
        [Test]
        public void FindCallSites_AsyncCaller_ReportsLogicalOwnerMethodKey()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                nameof(HotReloadCallSiteScannerFixture.CalledFromAsyncMethod));

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::AsyncCaller()"));
            Assert.That(
                hits[0].CallerMethodName,
                Is.EqualTo(nameof(HotReloadCallSiteScannerFixture.AsyncCaller)));
        }

        private static List<HotReloadCallSiteScanner.CallSiteHit> FindHits(string methodName)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);

            HotReloadCallSiteScanner.CompiledMethodIdentity target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    FixtureTypeMetadataName,
                    methodName,
                    Array.Empty<string>());

            return HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[] { target });
        }
    }
}
