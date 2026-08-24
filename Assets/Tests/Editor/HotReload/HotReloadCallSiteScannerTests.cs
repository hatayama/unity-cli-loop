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
                Array.Empty<string>(),
                0);

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
                Array.Empty<string>(),
                0);

            Assert.That(hits, Is.Empty);
        }

        /// <summary>
        /// What: a selected assembly with no compiled dll is reported so callers cannot be assumed complete.
        /// </summary>
        [Test]
        public void FindCallSites_MissingSelectedAssembly_ReportsAssemblyName()
        {
            const string missingAssemblyName = "MissingCompiledAssembly";
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadCallSiteScanner.CompiledMethodIdentity target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    missingAssemblyName,
                    FixtureTypeMetadataName,
                    nameof(HotReloadCallSiteScannerFixture.NeverCalled),
                    Array.Empty<string>(),
                    0);

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result =
                HotReloadCallSiteScanner.FindCallSites(projectRoot, new[] { target });

            Assert.That(result.MissingScanAssemblyNames, Is.EqualTo(new[] { missingAssemblyName }));
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
                Array.Empty<string>(),
                0);

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
                Array.Empty<string>(),
                0);

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
                Array.Empty<string>(),
                0);

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
                Array.Empty<string>(),
                1);

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
                new[] { "System.Int32" },
                0);

            Assert.That(hits, Is.Empty);
        }

        /// <summary>
        /// What: one scan with two targets attributes each hit to the matching TargetMethodKey.
        /// </summary>
        [Test]
        public void FindCallSites_TwoTargets_AttributesEachHitToMatchingTargetKey()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            string ordinaryTargetKey = FixtureTypeMetadataName + "::CalledFromOrdinaryMethod()";
            string delegateTargetKey = FixtureTypeMetadataName + "::CalledOnlyViaDelegate()";

            List<HotReloadCallSiteScanner.CallSiteHit> hits = HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[]
                {
                    new HotReloadCallSiteScanner.CompiledMethodIdentity(
                        assemblyName,
                        FixtureTypeMetadataName,
                        nameof(HotReloadCallSiteScannerFixture.CalledFromOrdinaryMethod),
                        Array.Empty<string>(),
                        0),
                    new HotReloadCallSiteScanner.CompiledMethodIdentity(
                        assemblyName,
                        FixtureTypeMetadataName,
                        nameof(HotReloadCallSiteScannerFixture.CalledOnlyViaDelegate),
                        Array.Empty<string>(),
                        0)
                }).Hits;

            Assert.That(hits.Count, Is.EqualTo(2));
            HotReloadCallSiteScanner.CallSiteHit ordinaryHit = null;
            HotReloadCallSiteScanner.CallSiteHit delegateHit = null;
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                if (hit.TargetMethodKey == ordinaryTargetKey)
                {
                    ordinaryHit = hit;
                }

                if (hit.TargetMethodKey == delegateTargetKey)
                {
                    delegateHit = hit;
                }
            }

            Assert.That(ordinaryHit, Is.Not.Null, "Ordinary target must own its hit.");
            Assert.That(
                ordinaryHit.CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::OrdinaryCaller()"));
            Assert.That(delegateHit, Is.Not.Null, "Delegate target must own its hit.");
            Assert.That(
                delegateHit.CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::CaptureDelegate()"));
        }

        /// <summary>
        /// What: a caller in another project assembly that references the target DLL is
        /// reported, so the scanner does not miss cross-assembly call sites.
        /// </summary>
        [Test]
        public void FindCallSites_CrossAssemblyCaller_ReportsReferencedAssemblyHit()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromCrossAssembly),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerAssemblyName,
                Is.EqualTo("UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly"));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(
                    "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                    + ".HotReloadCallSiteCrossAssemblyCaller::Call()"));
        }

        /// <summary>
        /// What: a generic Caller&lt;T&gt;(int) call site uses a different method key than
        /// the non-generic Caller(int), so arity collisions cannot cover the wrong caller.
        /// </summary>
        [Test]
        public void FindCallSites_GenericArityCaller_KeyDiffersFromNonGenericCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromGenericArityCaller),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::Caller`1(System.Int32)"));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.Not.EqualTo(FixtureTypeMetadataName + "::Caller(System.Int32)"));
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
                new[] { "System.Int32" },
                0);

            Assert.That(hits, Is.Empty);
        }

        private static List<HotReloadCallSiteScanner.CallSiteHit> FindHits(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
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
                    parameterTypeFullNames,
                    genericArity);

            return HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[] { target }).Hits;
        }
    }
}
