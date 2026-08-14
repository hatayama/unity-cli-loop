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

        private const string GenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GenericHost`1";

        /// <summary>
        /// What: a method called from an ordinary method is reported with that caller's method key.
        /// </summary>
        [Test]
        public void FindCallSites_OrdinaryCaller_ReportsCallerMethodKey()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromOrdinaryMethod),
                Array.Empty<string>());

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
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.NeverCalled),
                Array.Empty<string>());

            Assert.That(hits, Is.Empty);
        }

        /// <summary>
        /// What: a method referenced only by delegate assignment is found via Ldftn.
        /// </summary>
        [Test]
        public void FindCallSites_DelegateAssignment_ReportsLdftnCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledOnlyViaDelegate),
                Array.Empty<string>());

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
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromAsyncMethod),
                Array.Empty<string>());

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::AsyncCaller()"));
            Assert.That(
                hits[0].CallerMethodName,
                Is.EqualTo(nameof(HotReloadCallSiteScannerFixture.AsyncCaller)));
        }

        /// <summary>
        /// What: a call through GenericHost&lt;int&gt; still hits the open GenericHost`1 Target.
        /// </summary>
        [Test]
        public void FindCallSites_GenericTypeInstantiation_ReportsCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                GenericHostTypeMetadataName,
                nameof(GenericHost<int>.Target),
                Array.Empty<string>());

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::CallGenericHostTarget()"));
        }

        /// <summary>
        /// What: an instantiated generic method is found both via Call and via Ldftn.
        /// </summary>
        [Test]
        public void FindCallSites_GenericMethodInstantiation_ReportsCallAndLdftn()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.GenericMethodTarget),
                Array.Empty<string>());

            List<string> keys = hits.ConvertAll(hit => hit.CallerMethodKey);
            Assert.That(keys, Does.Contain(FixtureTypeMetadataName + "::CallGenericMethodTarget()"));
            Assert.That(keys, Does.Contain(FixtureTypeMetadataName + "::CaptureGenericMethodTarget()"));
            Assert.That(hits.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a recursive call inside the target itself is not reported as a caller.
        /// </summary>
        [Test]
        public void FindCallSites_OrdinarySelfRecursion_ReturnsEmpty()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.SelfRecursive),
                new[] { "System.Int32" });

            Assert.That(hits, Is.Empty);
        }

        /// <summary>
        /// What: an async method awaiting itself is not reported after logical-owner resolution.
        /// </summary>
        [Test]
        public void FindCallSites_AsyncSelfRecursion_ReturnsEmpty()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.AsyncSelfRecursive),
                new[] { "System.Int32" });

            Assert.That(hits, Is.Empty);
        }

        private static List<HotReloadCallSiteScanner.CallSiteHit> FindHits(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);

            HotReloadCallSiteScanner.CompiledMethodIdentity target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    typeMetadataName,
                    methodName,
                    parameterTypeFullNames);

            return HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[] { target });
        }
    }
}
