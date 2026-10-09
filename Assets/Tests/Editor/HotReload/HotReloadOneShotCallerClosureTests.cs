using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// How the caller closure treats the scan results of its deeper levels, driven by stub scans.
    /// </summary>
    public sealed class HotReloadOneShotCallerClosureTests
    {
        private const string HelperTypeMetadataName = "Callers.Helper";
        private const string HelperMethodName = "Run";

        /// <summary>
        /// What: a direct caller that is itself called only from Awake is proven at the second
        /// level and the Awake caller is returned as the root.
        /// </summary>
        [Test]
        public void Resolve_LevelTwoCallerIsAwake_ReturnsThatRoot()
        {
            List<OneShotCallerClassification> roots = HotReloadOneShotCallerClosure.Resolve(
                new[] { CreateDirectHelperHit() },
                (assemblyName, identities) => new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                    new List<HotReloadCallSiteScanner.CallSiteHit> { CreateAwakeCallerOfHelperHit() },
                    new List<string>()),
                hit => hit.CallerMethodName == "Awake");

            Assert.That(roots, Is.Not.Null);
            Assert.That(roots.Count, Is.EqualTo(1));
            Assert.That(roots[0].MethodName, Is.EqualTo("Awake"));
        }

        /// <summary>
        /// What: a second-level scan that the load budget left incomplete aborts the closure even
        /// though the hits it returned would have proven an Awake root.
        /// </summary>
        [Test]
        public void Resolve_LevelTwoScanIsIncompleteBecauseOfUnreadAssemblies_ReturnsNull()
        {
            List<HotReloadCallSiteScanner.CompiledMethodIdentity[]> scannedIdentities =
                new List<HotReloadCallSiteScanner.CompiledMethodIdentity[]>();

            List<OneShotCallerClassification> roots = HotReloadOneShotCallerClosure.Resolve(
                new[] { CreateDirectHelperHit() },
                (assemblyName, identities) =>
                {
                    scannedIdentities.Add(identities);
                    return new HotReloadCallSiteScanner.HotReloadCallSiteScanResult(
                        new List<HotReloadCallSiteScanner.CallSiteHit> { CreateAwakeCallerOfHelperHit() },
                        new List<string>(),
                        0,
                        null,
                        new List<string> { "Assembly.Unread" });
                },
                hit => hit.CallerMethodName == "Awake");

            Assert.That(roots, Is.Null);
            Assert.That(scannedIdentities.Count, Is.EqualTo(1));
            Assert.That(scannedIdentities[0].Length, Is.EqualTo(1));
            Assert.That(scannedIdentities[0][0].TypeMetadataName.Value, Is.EqualTo(HelperTypeMetadataName));
            Assert.That(scannedIdentities[0][0].MethodName, Is.EqualTo(HelperMethodName));
        }

        private static HotReloadCallSiteScanner.CallSiteHit CreateDirectHelperHit()
        {
            return new HotReloadCallSiteScanner.CallSiteHit
            {
                CallerAssemblyName = "Assembly.Callers",
                CallerTypeMetadataName = new HotReloadMetadataTypeName(HelperTypeMetadataName),
                CallerMethodName = HelperMethodName,
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerGenericArity = 0,
                CallerMethodKey = "Callers.Helper::Run()",
                TargetMethodKey = "Edited.Type::Target()",
                IsFunctionPointerLoad = false
            };
        }

        private static HotReloadCallSiteScanner.CallSiteHit CreateAwakeCallerOfHelperHit()
        {
            return new HotReloadCallSiteScanner.CallSiteHit
            {
                CallerAssemblyName = "Assembly.Callers",
                CallerTypeMetadataName = new HotReloadMetadataTypeName("Callers.Mono"),
                CallerMethodName = "Awake",
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerGenericArity = 0,
                CallerMethodKey = "Callers.Mono::Awake()",
                TargetMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    HelperTypeMetadataName,
                    HelperMethodName,
                    Array.Empty<string>(),
                    0),
                IsFunctionPointerLoad = false
            };
        }
    }
}
