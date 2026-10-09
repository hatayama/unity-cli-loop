using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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

        private const string NestedCallerHostTypeMetadataName =
            FixtureTypeMetadataName + "/NestedCallerHost";

        private const string GenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.GenericHost`1";

        private const string CrossAssemblyTargetTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerCrossAssemblyTarget";

        private const string QualifiedCallerIdentityTargetTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadQualifiedCallerIdentityTarget";

        private const string CrossAssemblyGenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossAssemblyGenericHost`1";

        private const string CrossAssemblyNestedTargetTypeMetadataName =
            CrossAssemblyTargetTypeMetadataName + "/Nested";

        private const string CrossAssemblyCallerAssemblyName =
            "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private const string CrossAssemblyCallerTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteCrossAssemblyCaller";

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
                    new HotReloadMetadataTypeName(FixtureTypeMetadataName),
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
            Assert.That(hits[0].IsFunctionPointerLoad, Is.True);
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
                        new HotReloadMetadataTypeName(FixtureTypeMetadataName),
                        nameof(HotReloadCallSiteScannerFixture.CalledFromOrdinaryMethod),
                        Array.Empty<string>(),
                        0),
                    new HotReloadCallSiteScanner.CompiledMethodIdentity(
                        assemblyName,
                        new HotReloadMetadataTypeName(FixtureTypeMetadataName),
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
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
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
        /// What: callers with the same metadata name and wire key are reported with their distinct assemblies.
        /// </summary>
        [Test]
        public void FindCallSites_SameKeyCallersAcrossAssemblies_ReportsBothCallerAssemblies()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                QualifiedCallerIdentityTargetTypeMetadataName,
                nameof(HotReloadQualifiedCallerIdentityTarget.Called),
                Array.Empty<string>(),
                0);
            List<string> callerAssemblyNames = new List<string>();
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                callerAssemblyNames.Add(hit.CallerAssemblyName);
                Assert.That(
                    hit.CallerMethodKey,
                    Is.EqualTo(
                        "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                        + ".HotReloadQualifiedCallerIdentityCaller::Call()"));
            }

            Assert.That(hits, Has.Count.EqualTo(2));
            Assert.That(
                callerAssemblyNames,
                Does.Contain("UnityCLILoop.Tests.Editor.HotReload"));
            Assert.That(
                callerAssemblyNames,
                Does.Contain("UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly"));
        }

        /// <summary>
        /// What: a same-name method in a different assembly is not attributed to the main target.
        /// </summary>
        [Test]
        public void FindCallSites_SameFullNameForeignAssemblyTarget_ExcludesForeignCallSite()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromCrossAssembly),
                Array.Empty<string>(),
                0);

            Assert.That(hits, Is.Empty);
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

        /// <summary>
        /// What: a call from a nested type reports the caller type in the metadata spelling, and
        /// the wire method key it is assembled into keeps that same separator.
        /// </summary>
        [Test]
        public void FindCallSites_NestedCaller_KeepsTheMetadataSeparator()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.CalledFromNestedType),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerTypeMetadataName.Value,
                Is.EqualTo(NestedCallerHostTypeMetadataName));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(NestedCallerHostTypeMetadataName + "::NestedCaller()"));
        }

        /// <summary>
        /// What: a method of a nested type, named in the metadata spelling, is found through the
        /// call-site index and reports its caller in the outer type.
        /// </summary>
        [Test]
        public void FindCallSites_NestedTarget_ReportsTheCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                NestedCallerHostTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.NestedCallerHost.CalledFromOuterType),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].TargetMethodKey,
                Is.EqualTo(NestedCallerHostTypeMetadataName + "::CalledFromOuterType()"));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::CallNestedTarget()"));
        }

        /// <summary>
        /// What: a scan compares only the call sites the index files under its target's type and
        /// method name, not every call site of the scanned assemblies.
        /// </summary>
        [Test]
        public void FindCallSites_ExaminesOnlyTheCallSitesFiledUnderTheTargets()
        {
            const string targetMethodName = nameof(HotReloadCallSiteScannerFixture.CalledFromOrdinaryMethod);
            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = Scan(
                FixtureTypeMetadataName,
                targetMethodName,
                Array.Empty<string>(),
                0);

            HotReloadCompiledCallSiteCache.Entry compiled =
                HotReloadCompiledCallSiteCache.Shared.GetOrLoad(GetTestAssemblyDllPath());
            int filedUnderTarget = compiled.LookupCallSiteIndices(FixtureTypeMetadataName, targetMethodName).Count;

            Assert.That(result.Hits.Count, Is.EqualTo(1));
            // Why the test assembly's bucket alone: the other scanned assembly never calls this
            // fixture method, so its bucket under the same key is empty.
            Assert.That(result.ExaminedCallSiteCount, Is.EqualTo(filedUnderTarget));
            Assert.That(
                result.ExaminedCallSiteCount,
                Is.LessThan(compiled.CallSites.Count),
                "A scan must not walk every call site of the assembly.");
        }

        /// <summary>
        /// What: a call site whose type-parameter argument matches two targets is reported once,
        /// for whichever of them comes first in the given order, so one compiled call is never
        /// counted twice.
        /// </summary>
        [Test]
        public void FindCallSites_TwoTargetsMatchingOneCallSite_ReportsOneHitForTheFirstTarget()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            HotReloadCallSiteScanner.CompiledMethodIdentity typeParameterTarget =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    new HotReloadMetadataTypeName(FixtureTypeMetadataName),
                    nameof(HotReloadCallSiteScannerFixture.GenericParameterTarget),
                    new[] { "T" },
                    1);
            HotReloadCallSiteScanner.CompiledMethodIdentity int32Target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    new HotReloadMetadataTypeName(FixtureTypeMetadataName),
                    nameof(HotReloadCallSiteScannerFixture.GenericParameterTarget),
                    new[] { "System.Int32" },
                    1);

            List<HotReloadCallSiteScanner.CallSiteHit> hits = HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[] { typeParameterTarget, int32Target }).Hits;
            List<HotReloadCallSiteScanner.CallSiteHit> reversedHits = HotReloadCallSiteScanner.FindCallSites(
                projectRoot,
                new[] { int32Target, typeParameterTarget }).Hits;

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].TargetMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::GenericParameterTarget`1(T)"));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::CallGenericParameterTarget()"));
            Assert.That(reversedHits.Count, Is.EqualTo(1));
            Assert.That(
                reversedHits[0].TargetMethodKey,
                Is.EqualTo(FixtureTypeMetadataName + "::GenericParameterTarget`1(System.Int32)"));
        }

        /// <summary>
        /// What: a call in another assembly through a constructed generic type is reported, so an
        /// assembly whose MemberRef table names the method only via the instantiation is still read.
        /// </summary>
        [Test]
        public void FindCallSites_CrossAssemblyGenericTypeInstantiation_ReportsCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                CrossAssemblyGenericHostTypeMetadataName,
                nameof(HotReloadCrossAssemblyGenericHost<int>.Target),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].CallerAssemblyName, Is.EqualTo(CrossAssemblyCallerAssemblyName));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(CrossAssemblyCallerTypeMetadataName + "::CallGenericHostTarget()"));
        }

        /// <summary>
        /// What: an instantiated generic method of another assembly is reported both via Call and via
        /// Ldftn, so a MethodSpec operand is traced back to the MemberRef row it instantiates.
        /// </summary>
        [Test]
        public void FindCallSites_CrossAssemblyGenericMethodInstantiation_ReportsCallAndLdftn()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.GenericMethod),
                Array.Empty<string>(),
                1);

            Assert.That(hits.Count, Is.EqualTo(2));
            HotReloadCallSiteScanner.CallSiteHit call = hits.Find(hit => !hit.IsFunctionPointerLoad);
            HotReloadCallSiteScanner.CallSiteHit load = hits.Find(hit => hit.IsFunctionPointerLoad);
            Assert.That(call.CallerAssemblyName, Is.EqualTo(CrossAssemblyCallerAssemblyName));
            Assert.That(load.CallerAssemblyName, Is.EqualTo(CrossAssemblyCallerAssemblyName));
            Assert.That(
                call.CallerMethodKey,
                Is.EqualTo(CrossAssemblyCallerTypeMetadataName + "::CallGenericMethodTarget()"));
            Assert.That(
                load.CallerMethodKey,
                Is.EqualTo(CrossAssemblyCallerTypeMetadataName + "::CaptureGenericMethodTarget()"));
        }

        /// <summary>
        /// What: a call in another assembly to a method of a nested type is reported, so the nested
        /// type's metadata name with '/' finds the caller's MemberRef row.
        /// </summary>
        [Test]
        public void FindCallSites_CrossAssemblyNestedTarget_ReportsCaller()
        {
            List<HotReloadCallSiteScanner.CallSiteHit> hits = FindHits(
                CrossAssemblyNestedTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Nested.CalledFromOtherAssembly),
                Array.Empty<string>(),
                0);

            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(
                hits[0].CallerMethodKey,
                Is.EqualTo(CrossAssemblyCallerTypeMetadataName + "::CallNestedTarget()"));
        }

        /// <summary>
        /// What: a scan of a method no other assembly names reads only the target assembly and
        /// lists the referencing assembly as skipped.
        /// </summary>
        [Test]
        public async Task FindCallSites_TargetNoOtherAssemblyMentions_ReadsOnlyTheTargetAssembly()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = Scan(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.NeverCalled),
                Array.Empty<string>(),
                0);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(1));
            Assert.That(result.SkippedScanAssemblyNames, Does.Contain(CrossAssemblyCallerAssemblyName));
            Assert.That(result.Hits, Is.Empty);
        }

        /// <summary>
        /// What: a scan of a method another assembly calls reads that assembly too and does not list
        /// it as skipped.
        /// </summary>
        [Test]
        public async Task FindCallSites_CrossAssemblyCaller_ReadsTheCallerAssembly()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = Scan(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                Array.Empty<string>(),
                0);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(2));
            Assert.That(
                result.SkippedScanAssemblyNames,
                Does.Not.Contain(CrossAssemblyCallerAssemblyName));
            Assert.That(result.Hits.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: with a budget of one load and an empty cache, a scan reads the target assembly,
        /// refuses the caller assembly, lists it as unread, and reports itself incomplete.
        /// </summary>
        [Test]
        public async Task FindCallSites_WithLoadBudgetOfOne_ReadsOnlyTheTargetAssemblyAndRefusesTheCaller()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadCallSiteLoadBudget budget = new HotReloadCallSiteLoadBudget(1);

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = ScanWithBudget(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                budget);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(1));
            Assert.That(result.UnreadScanAssemblyNames, Is.EquivalentTo(new[] { CrossAssemblyCallerAssemblyName }));
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.IsIncomplete, Is.True);
            Assert.That(budget.RemainingLoads, Is.EqualTo(0));
            Assert.That(budget.RefusedAssemblyNames, Is.EquivalentTo(new[] { CrossAssemblyCallerAssemblyName }));
        }

        /// <summary>
        /// What: with no load left and an empty cache, a scan reads nothing and lists both the
        /// target assembly and the caller assembly as unread.
        /// </summary>
        [Test]
        public async Task FindCallSites_WithExhaustedLoadBudget_ReadsNothingAndListsBothAsUnread()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadCallSiteLoadBudget budget = new HotReloadCallSiteLoadBudget(0);

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = ScanWithBudget(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                budget);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(0));
            Assert.That(
                result.UnreadScanAssemblyNames,
                Is.EquivalentTo(new[] { GetTestAssemblyName(), CrossAssemblyCallerAssemblyName }));
            Assert.That(result.Hits, Is.Empty);
            Assert.That(result.IsIncomplete, Is.True);
        }

        /// <summary>
        /// What: an assembly whose MemberRef table names no target is skipped before the budget is
        /// asked, so with no load left it is listed as skipped, not as unread; only the target
        /// assembly, which is always walked, is unread.
        /// </summary>
        [Test]
        public async Task FindCallSites_WithExhaustedLoadBudget_StillSkipsAssembliesThatNameNoTarget()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadCallSiteLoadBudget budget = new HotReloadCallSiteLoadBudget(0);

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = ScanWithBudget(
                FixtureTypeMetadataName,
                nameof(HotReloadCallSiteScannerFixture.NeverCalled),
                budget);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(0));
            Assert.That(result.SkippedScanAssemblyNames, Does.Contain(CrossAssemblyCallerAssemblyName));
            Assert.That(result.UnreadScanAssemblyNames, Does.Not.Contain(CrossAssemblyCallerAssemblyName));
            Assert.That(result.UnreadScanAssemblyNames, Does.Contain(GetTestAssemblyName()));
        }

        /// <summary>
        /// What: an assembly that two scans of the same run both fail to read is listed once in
        /// the budget's refused assemblies, so the vibe log names each refused dll once, and its
        /// dll path is kept beside the name for the backfill.
        /// </summary>
        [Test]
        public async Task FindCallSites_AssemblyRefusedByTwoScans_IsListedOnceInTheBudget()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            HotReloadCallSiteLoadBudget budget = new HotReloadCallSiteLoadBudget(0);

            ScanWithBudget(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                budget);
            ScanWithBudget(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                budget);

            Assert.That(
                budget.RefusedAssemblyNames,
                Is.EquivalentTo(new[] { GetTestAssemblyName(), CrossAssemblyCallerAssemblyName }));
            List<string> refusedDllFileNames = new List<string>();
            foreach (string dllPath in budget.RefusedDllPaths)
            {
                refusedDllFileNames.Add(Path.GetFileName(dllPath));
            }

            Assert.That(
                refusedDllFileNames,
                Is.EquivalentTo(new[] { GetTestAssemblyName() + ".dll", CrossAssemblyCallerAssemblyName + ".dll" }));
        }

        /// <summary>
        /// What: assemblies already in the cache are served without consuming the budget, so a
        /// scan with no load left is still complete when every assembly it needs is cached.
        /// </summary>
        [Test]
        public async Task FindCallSites_CachedAssembliesDoNotConsumeTheLoadBudget()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            Scan(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                Array.Empty<string>(),
                0);
            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(2));
            HotReloadCallSiteLoadBudget budget = new HotReloadCallSiteLoadBudget(0);

            HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = ScanWithBudget(
                CrossAssemblyTargetTypeMetadataName,
                nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                budget);

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(2));
            Assert.That(result.Hits.Count, Is.EqualTo(1));
            Assert.That(result.IsIncomplete, Is.False);
            Assert.That(result.UnreadScanAssemblyNames, Is.Empty);
            Assert.That(budget.RemainingLoads, Is.EqualTo(0));
        }

        /// <summary>
        /// What: one run shares a single load budget across the candidate assemblies it scans, so
        /// two candidates in different uncached assemblies read one dll in total. A budget created
        /// per scan call would read two.
        /// </summary>
        [Test]
        public async Task ApplyOneShotCallerNotes_WithFreshCache_SharesOneLoadBudgetAcrossCandidateAssemblies()
        {
            await StopInstalledWarmUpAsync();
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                false);
            HotReloadMethodOutcome targetOutcome = HotReloadMethodOutcome.Patched("Type.Called", "Assets/Test.cs");
            HotReloadMethodOutcome callerOutcome = HotReloadMethodOutcome.Patched("Type.Call", "Assets/Test2.cs");
            run.OneShotCallerNoteCandidates.Add(new HotReloadOneShotCallerNoteEnricher.Candidate(
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    GetTestAssemblyName(),
                    new HotReloadMetadataTypeName(CrossAssemblyTargetTypeMetadataName),
                    nameof(HotReloadCallSiteScannerCrossAssemblyTarget.Called),
                    Array.Empty<string>(),
                    0),
                targetOutcome));
            run.OneShotCallerNoteCandidates.Add(new HotReloadOneShotCallerNoteEnricher.Candidate(
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    CrossAssemblyCallerAssemblyName,
                    new HotReloadMetadataTypeName(CrossAssemblyCallerTypeMetadataName),
                    "Call",
                    Array.Empty<string>(),
                    0),
                callerOutcome));

            run.ApplyOneShotCallerNotes(GetProjectRoot(), "test-correlation");

            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount - before, Is.EqualTo(1));
        }

        /// <summary>
        /// What: the referencing dlls of the test assembly include the cross-assembly caller's dll
        /// and never the test assembly's own dll.
        /// </summary>
        [Test]
        public void CollectReferencingDllPaths_ReturnsTheCrossAssemblyCallerAndNotTheTargetItself()
        {
            string projectRoot = GetProjectRoot();
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);

            IReadOnlyList<string> dllPaths = HotReloadCallSiteScanner.CollectReferencingDllPaths(
                projectRoot,
                GetTestAssemblyName());

            Assert.That(dllPaths, Does.Contain(layout.DllPath(CrossAssemblyCallerAssemblyName)));
            Assert.That(dllPaths, Does.Not.Contain(layout.DllPath(GetTestAssemblyName())));
        }

        // Why: the installed warm-up and the caller-note backfill load into the shared cache on a
        // pool thread, and a read they make between Clear and the scan would be counted as the scan's.
        private static Task StopInstalledWarmUpAsync()
        {
            HotReloadServices installed = HotReloadCompositionRoot.Services;
            return Task.WhenAll(
                installed.WarmUp.Shutdown(HotReloadConstants.WarmUpShutdownTriggerTestScope),
                installed.CallSiteBackfill.Shutdown(HotReloadConstants.WarmUpShutdownTriggerTestScope));
        }

        private static List<HotReloadCallSiteScanner.CallSiteHit> FindHits(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            return Scan(typeMetadataName, methodName, parameterTypeFullNames, genericArity).Hits;
        }

        private static HotReloadCallSiteScanner.HotReloadCallSiteScanResult Scan(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            HotReloadCallSiteScanner.CompiledMethodIdentity target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    GetTestAssemblyName(),
                    new HotReloadMetadataTypeName(typeMetadataName),
                    methodName,
                    parameterTypeFullNames,
                    genericArity);

            return HotReloadCallSiteScanner.FindCallSites(
                GetProjectRoot(),
                new[] { target });
        }

        private static HotReloadCallSiteScanner.HotReloadCallSiteScanResult ScanWithBudget(
            string typeMetadataName,
            string methodName,
            HotReloadCallSiteLoadBudget loadBudget)
        {
            HotReloadCallSiteScanner.CompiledMethodIdentity target =
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    GetTestAssemblyName(),
                    new HotReloadMetadataTypeName(typeMetadataName),
                    methodName,
                    Array.Empty<string>(),
                    0);

            return HotReloadCallSiteScanner.FindCallSites(
                GetProjectRoot(),
                new[] { target },
                loadBudget);
        }

        // The same path FindCallSites reads, so the shared cache returns the entry the scan used.
        private static string GetTestAssemblyDllPath()
        {
            return Path.Combine(
                GetProjectRoot(),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                GetTestAssemblyName() + HotReloadConstants.CompiledAssemblyExtension);
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string GetTestAssemblyName()
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            return Path.GetFileNameWithoutExtension(rawAssemblyName);
        }
    }
}
