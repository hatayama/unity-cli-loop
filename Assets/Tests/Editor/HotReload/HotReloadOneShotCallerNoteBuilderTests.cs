using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEngine;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for caller-aware one-shot lifecycle notes.
    /// </summary>
    public sealed class HotReloadOneShotCallerNoteBuilderTests
    {
        /// <summary>
        /// What: a caller whose method name is not a Unity one-shot message is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_NonLifecycleName_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ValidLifecycleFixture), "Update"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a parameterless void Awake declared on a MonoBehaviour is classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_ValidAwake_ReturnsTrue()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ValidLifecycleFixture), "Awake"));

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// What: a caller named in metadata form is still found by reflection, so a nested
        /// MonoBehaviour whose name uses the metadata nesting separator is classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_NestedCallerInMetadataForm_ReturnsTrue()
        {
            string metadataTypeName = typeof(ValidLifecycleFixture).FullName.Replace('+', '/');

            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(metadataTypeName, "Awake"));

            Assert.That(metadataTypeName, Does.Contain("/"));
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// What: a parameterized caller does not match a separate parameterless lifecycle-named method.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_ParameterizedCallerWithMatchingName_ReturnsFalse()
        {
            HotReloadCallSiteHit hit = CreateHit(typeof(OverloadedLifecycleFixture), "Awake");
            hit.CallerParameterTypeFullNames = new[] { "System.Int32" };

            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(hit);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a generic caller does not match a separate non-generic lifecycle-named method.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_GenericCallerWithMatchingName_ReturnsFalse()
        {
            HotReloadCallSiteHit hit = CreateHit(typeof(OverloadedLifecycleFixture), "Awake");
            hit.CallerGenericArity = 1;

            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(hit);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: an unresolvable caller type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_UnresolvableType_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit("Missing.Namespace.Type", "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a non-MonoBehaviour caller type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_NonMonoBehaviour_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(NonMonoBehaviourFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a lifecycle-named method with parameters is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_MethodWithParameter_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ParameterLifecycleFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a lifecycle-named method with a non-void return type is not classified.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_MethodWithReturnValue_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(ReturnValueLifecycleFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: a generic lifecycle-named method is not classified because Unity cannot dispatch it.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_GenericMethod_ReturnsFalse()
        {
            bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                CreateHit(typeof(GenericLifecycleFixture), "Awake"));

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// What: multiple generic lifecycle-named methods are rejected without throwing.
        /// </summary>
        [Test]
        public void IsOneShotLifecycleCaller_MultipleGenericMethods_ReturnsFalseWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                bool result = HotReloadOneShotCallerNoteEnricher.IsOneShotLifecycleCaller(
                    CreateHit(typeof(MultipleGenericLifecycleFixture), "Awake"));

                Assert.That(result, Is.False);
            });
        }

        /// <summary>
        /// What: an incomplete scan returns no caller-aware note for its requests.
        /// </summary>
        [Test]
        public void BuildNotes_MissingScanAssembly_SuppressesNotes()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest("Assembly.One", "Type.SetUp")
                };

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteHit>(),
                    new List<string> { assemblyName }));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a scan the load budget left incomplete suppresses the note even when the hits it
        /// did find would have proven an Awake-only caller.
        /// </summary>
        [Test]
        public void BuildNotes_UnreadScanAssembly_SuppressesNotes()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest("Assembly.One", "Type.SetUp")
                };
            HotReloadCallSiteHit hit = CreateHit(typeof(ValidLifecycleFixture), "Awake");
            hit.TargetMethodKey = HotReloadMethodKeys.BuildMethodKeyParts("Type", "SetUp", Array.Empty<string>(), 0);

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteHit> { hit },
                    new List<string>(),
                    0,
                    null,
                    new List<string> { assemblyName }));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: requests with separate target assemblies are scanned in separate fake calls.
        /// </summary>
        [Test]
        public void BuildNotes_DifferentTargetAssemblies_ScansEachAssemblySeparately()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest("Assembly.One", "Type.First"),
                    CreateRequest("Assembly.Two", "Type.Second")
                };
            List<HotReloadCompiledMethodIdentity[]> calls =
                new List<HotReloadCompiledMethodIdentity[]>();

            HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) =>
                {
                    calls.Add(identities);
                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteHit>(),
                        new List<string>());
                });

            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(calls[0].Length, Is.EqualTo(1));
            Assert.That(calls[1].Length, Is.EqualTo(1));
            Assert.That(calls[0][0].AssemblyName, Is.Not.EqualTo(calls[1][0].AssemblyName));
        }

        /// <summary>
        /// What: a proven Awake-only caller returns the full indirect note for the request.
        /// </summary>
        [Test]
        public void BuildNotes_OnlyValidAwakeCaller_ReturnsIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest(typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name, "Type.SetUp")
                };
            HotReloadCallSiteHit hit = CreateHit(typeof(ValidLifecycleFixture), "Awake");
            hit.TargetMethodKey = "Type::SetUp()";

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteHit> { hit },
                    new List<string>()));

            Assert.That(
                notes[0],
                Is.EqualTo(
                    "Type.SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a function-pointer load suppresses the note before its Awake caller is classified.
        /// </summary>
        [Test]
        public void BuildNotes_FunctionPointerLoadHit_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest(typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name, "Type.SetUp")
                };
            HotReloadCallSiteHit hit = CreateHit(typeof(ValidLifecycleFixture), "Awake");
            hit.TargetMethodKey = "Type::SetUp()";
            hit.IsFunctionPointerLoad = true;

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteHit> { hit },
                    new List<string>()));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: compiled Awake-only callers return an indirect lifecycle note for the patched method.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledAwakeOnlyCaller_AddsIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("AwakeOnlyTarget", "OneShotCallerScannerFixture.AwakeOnlyTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(
                notes[0],
                Is.EqualTo(
                    "OneShotCallerScannerFixture.AwakeOnlyTarget() is called only from one-shot lifecycle "
                    + "method(s) (Awake) in the compiled assemblies; objects that already ran them will not run "
                    + "the patched body. It takes effect only for newly created objects, or run `uloop compile` "
                    + "and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a compiled ordinary caller suppresses the indirect lifecycle note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledOrdinaryCaller_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("MixedTarget", "OneShotCallerScannerFixture.MixedTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a compiled two-step Awake chain still produces the indirect lifecycle note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledChainedAwakeOnlyCaller_AddsIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("ChainedAwakeOnlyTarget", "OneShotCallerScannerFixture.ChainedAwakeOnlyTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(
                notes[0],
                Is.EqualTo(
                    "OneShotCallerScannerFixture.ChainedAwakeOnlyTarget() is called only from one-shot "
                    + "lifecycle method(s) (Awake) in the compiled assemblies; objects that already ran them "
                    + "will not run the patched body. It takes effect only for newly created objects, or run "
                    + "`uloop compile` and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a compiled Awake chain through a non-MonoBehaviour instance still produces the note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledNonMonoBehaviourChain_AddsIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerRequestForType(typeof(OneShotCallerChainHelper), "ConfigureTarget", "OneShotCallerChainHelper.ConfigureTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(
                notes[0],
                Is.EqualTo(
                    "OneShotCallerChainHelper.ConfigureTarget() is called only from one-shot lifecycle "
                    + "method(s) (Awake) in the compiled assemblies; objects that already ran them will not run "
                    + "the patched body. It takes effect only for newly created objects, or run `uloop compile` "
                    + "and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a compiled chain that also has a non-lifecycle root suppresses the note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledMixedChain_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("MixedChainTarget", "OneShotCallerScannerFixture.MixedChainTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a compiled chain whose intermediate is loaded as a function pointer suppresses the note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledDelegateChain_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("DelegateChainTarget", "OneShotCallerScannerFixture.DelegateChainTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a compiled chain that stops at an uncalled intermediate suppresses the note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledDeadEndChain_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("DeadEndTarget", "OneShotCallerScannerFixture.DeadEndTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a compiled chain deeper than MaxCallerDepth suppresses the note.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledDeepChain_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("DeepTarget", "OneShotCallerScannerFixture.DeepTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: a cycle between the target and an intermediate still keeps a proven Awake root.
        /// </summary>
        [Test]
        public void BuildNotes_CycleThroughIntermediate_AddsIndirectLifecycleNote()
        {
            string assemblyName = typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name;
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest(assemblyName, "Type.SetUp")
                };
            const string intermediateType = "Ns.Mid";
            const string intermediateName = "Run";
            string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                "Type",
                "SetUp",
                Array.Empty<string>(),
                0);
            string intermediateKey = HotReloadMethodKeys.BuildMethodKeyParts(
                intermediateType,
                intermediateName,
                Array.Empty<string>(),
                0);
            HotReloadCallSiteHit midToTarget = CreateKeyedHit(
                assemblyName,
                intermediateType,
                intermediateName,
                targetKey);
            HotReloadCallSiteHit awakeToMid = CreateKeyedHit(
                assemblyName,
                typeof(ValidLifecycleFixture).FullName,
                "Awake",
                intermediateKey);
            HotReloadCallSiteHit targetToMid = CreateKeyedHit(
                assemblyName,
                "Type",
                "SetUp",
                intermediateKey);

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (scannedAssembly, identities) =>
                {
                    if (identities.Length == 1 && identities[0].MethodName == intermediateName)
                    {
                        return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                            new List<HotReloadCallSiteHit> { awakeToMid, targetToMid },
                            new List<string>());
                    }

                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteHit> { midToTarget },
                        new List<string>());
                });

            Assert.That(
                notes[0],
                Is.EqualTo(
                    "Type.SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a missing scan assembly at depth two suppresses the note.
        /// </summary>
        [Test]
        public void BuildNotes_MissingScanAssemblyAtDepthTwo_SuppressesNotes()
        {
            string assemblyName = typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name;
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest(assemblyName, "Type.SetUp")
                };
            const string intermediateType = "Ns.Mid";
            const string intermediateName = "Run";
            string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                "Type",
                "SetUp",
                Array.Empty<string>(),
                0);
            HotReloadCallSiteHit midToTarget = CreateKeyedHit(
                assemblyName,
                intermediateType,
                intermediateName,
                targetKey);

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (scannedAssembly, identities) =>
                {
                    if (identities.Length == 1 && identities[0].MethodName == intermediateName)
                    {
                        return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                            new List<HotReloadCallSiteHit>(),
                            new List<string> { scannedAssembly });
                    }

                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteHit> { midToTarget },
                        new List<string>());
                });

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: identical caller keys in different assemblies stay distinct so a dead end is not dropped.
        /// </summary>
        [Test]
        public void BuildNotes_SameCallerKeyDifferentAssemblies_SuppressesNoteAndScansEachAssembly()
        {
            const string targetAssembly = "TargetAsm";
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateRequest(targetAssembly, "Type.SetUp")
                };
            const string helperType = "Ns.Helper";
            const string helperMethod = "Build";
            const string assemblyX = "AsmX";
            const string assemblyY = "AsmY";
            string helperKey = HotReloadMethodKeys.BuildMethodKeyParts(
                helperType,
                helperMethod,
                Array.Empty<string>(),
                0);
            string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                "Type",
                "SetUp",
                Array.Empty<string>(),
                0);
            HotReloadCallSiteHit assemblyXHit = CreateKeyedHit(
                assemblyX,
                helperType,
                helperMethod,
                targetKey);
            HotReloadCallSiteHit assemblyYHit = CreateKeyedHit(
                assemblyY,
                helperType,
                helperMethod,
                targetKey);
            List<HotReloadCompiledMethodIdentity[]> calls =
                new List<HotReloadCompiledMethodIdentity[]>();

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (scannedAssembly, identities) =>
                {
                    calls.Add(identities);
                    if (scannedAssembly == assemblyX)
                    {
                        return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                            new List<HotReloadCallSiteHit>
                            {
                                CreateKeyedHit(
                                    typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name,
                                    typeof(ValidLifecycleFixture).FullName,
                                    "Awake",
                                    helperKey)
                            },
                            new List<string>());
                    }

                    if (scannedAssembly == assemblyY)
                    {
                        return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                            new List<HotReloadCallSiteHit>(),
                            new List<string>());
                    }

                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteHit> { assemblyXHit, assemblyYHit },
                        new List<string>());
                });

            Assert.That(notes[0], Is.Null);
            Assert.That(calls.Count, Is.EqualTo(3));
            Assert.That(calls[1].Length, Is.EqualTo(1));
            Assert.That(calls[2].Length, Is.EqualTo(1));
            Assert.That(calls[1][0].AssemblyName, Is.Not.EqualTo(calls[2][0].AssemblyName));
            Assert.That(
                new[] { calls[1][0].AssemblyName, calls[2][0].AssemblyName },
                Is.EquivalentTo(new[] { assemblyX, assemblyY }));
        }

        /// <summary>
        /// What: a handler assigned as a delegate in Awake suppresses the indirect note because
        /// event-driven calls are not proven to be one-shot.
        /// </summary>
        [Test]
        public void BuildNotes_CompiledDelegateAssignment_SuppressesIndirectLifecycleNote()
        {
            List<HotReloadOneShotCallerNoteRequest> requests =
                new List<HotReloadOneShotCallerNoteRequest>
                {
                    CreateScannerFixtureRequest("DelegateAssignedTarget", "OneShotCallerScannerFixture.DelegateAssignedTarget()")
                };
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string[] notes = HotReloadOneShotCallerNoteEnricher.BuildNotes(
                requests,
                (assemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));

            Assert.That(notes[0], Is.Null);
        }

        /// <summary>
        /// What: WithLifecycleNote returns a copy that preserves the patched outcome fields.
        /// </summary>
        [Test]
        public void WithLifecycleNote_PatchedOutcome_PreservesOutcomeFields()
        {
            HotReloadMethodOutcome original = HotReloadMethodOutcome.Patched("Type.Method", "Assets/Test.cs");

            HotReloadMethodOutcome updated = original.WithLifecycleNote("note");

            Assert.That(updated, Is.Not.SameAs(original));
            Assert.That(updated.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(updated.Method, Is.EqualTo("Type.Method"));
            Assert.That(updated.FilePath, Is.EqualTo("Assets/Test.cs"));
            Assert.That(updated.LifecycleNote, Is.EqualTo("note"));
        }

        /// <summary>
        /// What: an indirect note with OnEnable among its callers says how to run the patched body
        /// on live objects by toggling enabled, and that Awake and Start do not run again that way.
        /// </summary>
        [Test]
        public void Build_OnEnableAmongCallers_AddsTheEnabledToggle()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("OnEnable", true),
                    new OneShotCallerClassification("Awake", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("Bind", callers);

            Assert.That(
                note,
                Does.EndWith(
                    " To run it on live objects from OnEnable or OnDisable, set the component's "
                    + "`enabled` to false and back to true (for example with `uloop execute-dynamic-code`); "
                    + "Awake and Start do not run again that way."));
        }

        /// <summary>
        /// What: an indirect note whose callers are only Awake or Start does not offer the enabled
        /// toggle, because toggling never runs those again.
        /// </summary>
        [Test]
        public void Build_OnlyAwakeAndStartCallers_DoesNotOfferTheEnabledToggle()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Awake", true),
                    new OneShotCallerClassification("Start", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("Bind", callers);

            Assert.That(note, Does.Not.Contain("`enabled`"));
        }

        /// <summary>
        /// What: one Awake caller produces the caller-aware lifecycle note.
        /// </summary>
        [Test]
        public void Build_OneAwakeCaller_ReturnsIndirectLifecycleNote()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Awake", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(
                note,
                Is.EqualTo(
                    "SetUp is called only from one-shot lifecycle method(s) (Awake) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: duplicate lifecycle callers are de-duplicated and ordered ordinally in the note.
        /// </summary>
        [Test]
        public void Build_DuplicateAwakeAndStartCallers_ReturnsSortedDistinctLifecycleNames()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Start", true),
                    new OneShotCallerClassification("Awake", true),
                    new OneShotCallerClassification("Awake", true)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(
                note,
                Is.EqualTo(
                    "SetUp is called only from one-shot lifecycle method(s) (Awake, Start) in the compiled "
                    + "assemblies; objects that already ran them will not run the patched body. It takes "
                    + "effect only for newly created objects, or run `uloop compile` and re-enter Play Mode."
                    + " Callers hot reload added or patched are not counted; if one of "
                    + "them calls it, the patched body already runs."));
        }

        /// <summary>
        /// What: a non-lifecycle caller suppresses the note so the only-callers claim is not guessed.
        /// </summary>
        [Test]
        public void Build_MixedCallerClassifications_ReturnsNull()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                new List<OneShotCallerClassification>
                {
                    new OneShotCallerClassification("Awake", true),
                    new OneShotCallerClassification("Update", false)
                };

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(note, Is.Null);
        }

        /// <summary>
        /// What: no compiled callers suppresses the note because non-IL reachability is unknown.
        /// </summary>
        [Test]
        public void Build_NoCallers_ReturnsNull()
        {
            IReadOnlyList<OneShotCallerClassification> callers =
                Array.Empty<OneShotCallerClassification>();

            string note = HotReloadOneShotCallerNoteBuilder.Build("SetUp", callers);

            Assert.That(note, Is.Null);
        }

        private static HotReloadCallSiteHit CreateHit(Type type, string methodName)
        {
            return CreateHit(type.FullName, methodName);
        }

        private static HotReloadCallSiteHit CreateHit(string typeMetadataName, string methodName)
        {
            return new HotReloadCallSiteHit
            {
                CallerAssemblyName = typeof(HotReloadOneShotCallerNoteBuilderTests).Assembly.GetName().Name,
                CallerTypeMetadataName = new HotReloadMetadataTypeName(typeMetadataName),
                CallerMethodName = methodName,
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerGenericArity = 0
            };
        }

        private static HotReloadOneShotCallerNoteRequest CreateRequest(
            string assemblyName,
            string method)
        {
            HotReloadCompiledMethodIdentity identity =
                new HotReloadCompiledMethodIdentity(
                    assemblyName,
                    new HotReloadMetadataTypeName("Type"),
                    "SetUp",
                    Array.Empty<string>(),
                    0);
            return new HotReloadOneShotCallerNoteRequest(identity, method);
        }

        private static HotReloadOneShotCallerNoteRequest CreateScannerFixtureRequest(
            string methodName,
            string method)
        {
            return CreateScannerRequestForType(typeof(OneShotCallerScannerFixture), methodName, method);
        }

        private static HotReloadOneShotCallerNoteRequest CreateScannerRequestForType(
            Type type,
            string methodName,
            string method)
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                "Assets/Tests/Editor/HotReload/HotReloadCallSiteScannerFixture.cs");
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            HotReloadCompiledMethodIdentity identity =
                new HotReloadCompiledMethodIdentity(
                    assemblyName,
                    new HotReloadMetadataTypeName(type.FullName),
                    methodName,
                    Array.Empty<string>(),
                    0);
            return new HotReloadOneShotCallerNoteRequest(identity, method);
        }

        private static HotReloadCallSiteHit CreateKeyedHit(
            string assemblyName,
            string typeMetadataName,
            string methodName,
            string targetMethodKey)
        {
            return new HotReloadCallSiteHit
            {
                CallerAssemblyName = assemblyName,
                CallerTypeMetadataName = new HotReloadMetadataTypeName(typeMetadataName),
                CallerMethodName = methodName,
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerGenericArity = 0,
                CallerMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    typeMetadataName,
                    methodName,
                    Array.Empty<string>(),
                    0),
                TargetMethodKey = targetMethodKey
            };
        }

        private sealed class ValidLifecycleFixture : MonoBehaviour
        {
            private void Awake()
            {
            }
        }

        /// <summary>
        /// Models user code shapes the classifier must reject; fixture overloads prove the production overload ban does not apply.
        /// </summary>
        private sealed class OverloadedLifecycleFixture : MonoBehaviour
        {
            private void Awake()
            {
            }

            private void Awake(int value)
            {
            }
        }

        private sealed class ParameterLifecycleFixture : MonoBehaviour
        {
            private void Awake(int value)
            {
            }
        }

        private sealed class ReturnValueLifecycleFixture : MonoBehaviour
        {
            private int Awake()
            {
                return 1;
            }
        }

        /// <summary>
        /// Models arbitrary user code shapes the classifier must reject without throwing.
        /// </summary>
        private sealed class GenericLifecycleFixture : MonoBehaviour
        {
            private void Awake<T>()
            {
            }
        }

        /// <summary>
        /// Models arbitrary user code shapes the classifier must reject without throwing.
        /// </summary>
        private sealed class MultipleGenericLifecycleFixture : MonoBehaviour
        {
            private void Awake<T>()
            {
            }

            private void Awake<T, U>()
            {
            }
        }

        private sealed class NonMonoBehaviourFixture
        {
            private void Awake()
            {
            }
        }
    }
}
