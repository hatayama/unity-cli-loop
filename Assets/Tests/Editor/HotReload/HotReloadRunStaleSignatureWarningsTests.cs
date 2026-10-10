using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure tests for the run-end stale-signature warnings: which recorded compiled callers are
    /// dropped because their patch is active when the run ends, and how the rest are worded.
    /// </summary>
    public class HotReloadRunStaleSignatureWarningsTests
    {
        private const string EditedAssemblyName = "EditedAssembly";
        private const string ExternalAssemblyName = "ExternalAssembly";
        private const string RemovedKey = "Example.Target::Removed()";
        private const string OtherRemovedKey = "Example.Target::AlsoRemoved()";
        private const string CallerType = "Example.Caller";
        private const string OtherCallerType = "Example.OtherCaller";
        private const string ThirdCallerType = "Example.ThirdCaller";

        /// <summary>
        /// What: a run that recorded no removed signature appends no warning.
        /// </summary>
        [Test]
        public void AppendTo_NothingRecorded_AppendsNothing()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = new HotReloadRunStaleSignatureWarnings();
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(warnings, new List<HotReloadActivePatchInfo>());

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: with no caller patched at the end of the run, the warning names every caller in
        /// the order they were recorded, worded exactly as the stale-signature format.
        /// </summary>
        [Test]
        public void AppendTo_NoCallerActive_KeepsTheExactWarningText()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(EditedAssemblyName, CallerType, "Call"),
                CreateHit(ExternalAssemblyName, OtherCallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(warnings, new List<HotReloadActivePatchInfo>());

            Assert.That(
                warnings,
                Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()", "Example.OtherCaller::Call()") }));
        }

        /// <summary>
        /// What: a signature whose every caller is patched when the run ends — one in the removed
        /// signature's own assembly and one in another assembly — appends no warning, whether the
        /// patch came from this run or an earlier one.
        /// </summary>
        [Test]
        public void AppendTo_AllCallersActive_AppendsNoWarning()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(EditedAssemblyName, CallerType, "Call"),
                CreateHit(ExternalAssemblyName, OtherCallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(EditedAssemblyName, "Example.Caller.Call()"),
                    CreateActivePatch(ExternalAssemblyName, "Example.OtherCaller.Call()")
                });

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: when some callers are patched at the end of the run, the warning names only the
        /// others, keeping their recorded order.
        /// </summary>
        [Test]
        public void AppendTo_SomeCallersActive_ListsTheRestInOrder()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(EditedAssemblyName, CallerType, "Call"),
                CreateHit(ExternalAssemblyName, OtherCallerType, "Call"),
                CreateHit(ExternalAssemblyName, ThirdCallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.OtherCaller.Call()")
                });

            Assert.That(
                warnings,
                Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()", "Example.ThirdCaller::Call()") }));
        }

        /// <summary>
        /// What: a patch on another method of the caller's type does not hide the caller, because
        /// only a patch on the caller itself stops its compiled body from running.
        /// </summary>
        [Test]
        public void AppendTo_CallerNotActive_KeepsItInWarning()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, CallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Caller.Unrelated()")
                });

            Assert.That(warnings, Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()") }));
        }

        /// <summary>
        /// What: a patch on a method with the caller's label in another assembly does not hide the
        /// caller, because the two assemblies declare different methods.
        /// </summary>
        [Test]
        public void AppendTo_SameLabelActiveInOtherAssembly_KeepsCaller()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, CallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(EditedAssemblyName, "Example.Caller.Call()")
                });

            Assert.That(warnings, Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()") }));
        }

        /// <summary>
        /// What: when two assemblies hold a caller with the same wire key and only one of them is
        /// patched, the other still names the key once, whichever of the two was recorded first.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AppendTo_SameWireKeyInTwoAssemblies_OneActive_ListsItOnce(bool activeCallerRecordedFirst)
        {
            HotReloadCallSiteHit activeCaller = CreateHit(ExternalAssemblyName, CallerType, "Call");
            HotReloadCallSiteHit compiledCaller = CreateHit(EditedAssemblyName, CallerType, "Call");
            HotReloadRunStaleSignatureWarnings staleWarnings = activeCallerRecordedFirst
                ? Record(RemovedKey, activeCaller, compiledCaller)
                : Record(RemovedKey, compiledCaller, activeCaller);
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Caller.Call()")
                });

            Assert.That(warnings, Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()") }));
        }

        /// <summary>
        /// What: when both same-key callers are patched, no caller is left and no warning is added.
        /// </summary>
        [Test]
        public void AppendTo_SameWireKeyInTwoAssemblies_BothActive_DropsWarning()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, CallerType, "Call"),
                CreateHit(EditedAssemblyName, CallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Caller.Call()"),
                    CreateActivePatch(EditedAssemblyName, "Example.Caller.Call()")
                });

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: the warning names a wire key shared by callers of two assemblies only once, since
        /// it shows keys, while each caller is still judged on its own assembly.
        /// </summary>
        [Test]
        public void AppendTo_SameWireKeyInTwoAssemblies_NeitherActive_ListsItOnce()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(EditedAssemblyName, CallerType, "Call"),
                CreateHit(ExternalAssemblyName, CallerType, "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(warnings, new List<HotReloadActivePatchInfo>());

            Assert.That(warnings, Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Caller::Call()") }));
        }

        /// <summary>
        /// What: a patched caller of a nested type is matched even though the scanner spells the
        /// type with the metadata '/' separator and the patch label uses reflection's '+'.
        /// </summary>
        [Test]
        public void AppendTo_NestedTypeCallerActive_IsOmitted()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, "Example.Outer/Inner", "Call"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Outer+Inner.Call()")
                });

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: a patched caller is dropped even when its hit loads the removed method as a
        /// function pointer, the same as a caller that the removed signature's group patches.
        /// </summary>
        [Test]
        public void AppendTo_FunctionPointerLoadOfActiveCaller_IsOmitted()
        {
            HotReloadCallSiteHit hit = CreateHit(ExternalAssemblyName, CallerType, "Call");
            hit.IsFunctionPointerLoad = true;
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(RemovedKey, hit);
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Caller.Call()")
                });

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: a call inside a lambda stays named under its compiler-generated method even when
        /// the method that declares the lambda is patched, because the scanner does not map
        /// closures to their owner.
        /// </summary>
        [Test]
        public void AppendTo_ClosureCallerOfActiveOwner_StaysListed()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, "Example.Host/<>c", "<Owner>b__0_0"));
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Host.Owner()")
                });

            Assert.That(
                warnings,
                Is.EqualTo(new[] { FormatWarning(RemovedKey, "Example.Host/<>c::<Owner>b__0_0()") }));
        }

        /// <summary>
        /// What: signatures recorded by two groups are filtered independently, so a patched caller
        /// of one does not hide a compiled caller of the other.
        /// </summary>
        [Test]
        public void AppendTo_TwoSignatures_FiltersEachIndependently()
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = Record(
                RemovedKey,
                CreateHit(ExternalAssemblyName, CallerType, "Call"));
            staleWarnings.AddRange(
                new List<HotReloadStaleSignatureCallSites>
                {
                    new HotReloadStaleSignatureCallSites(
                        OtherRemovedKey,
                        new List<HotReloadCallSiteHit>
                        {
                            CreateHit(ExternalAssemblyName, OtherCallerType, "Call")
                        })
                });
            List<string> warnings = new List<string>();

            staleWarnings.AppendTo(
                warnings,
                new List<HotReloadActivePatchInfo>
                {
                    CreateActivePatch(ExternalAssemblyName, "Example.Caller.Call()")
                });

            Assert.That(warnings, Is.EqualTo(new[] { FormatWarning(OtherRemovedKey, "Example.OtherCaller::Call()") }));
        }

        private static HotReloadRunStaleSignatureWarnings Record(
            string removedKey,
            params HotReloadCallSiteHit[] callers)
        {
            HotReloadRunStaleSignatureWarnings staleWarnings = new HotReloadRunStaleSignatureWarnings();
            staleWarnings.AddRange(
                new List<HotReloadStaleSignatureCallSites>
                {
                    new HotReloadStaleSignatureCallSites(
                        removedKey,
                        new List<HotReloadCallSiteHit>(callers))
                });
            return staleWarnings;
        }

        private static HotReloadCallSiteHit CreateHit(
            string assemblyName,
            string typeMetadataName,
            string methodName)
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
                TargetMethodKey = RemovedKey
            };
        }

        private static HotReloadActivePatchInfo CreateActivePatch(string assemblyName, string methodLabel)
        {
            return new HotReloadActivePatchInfo(methodLabel, "Assets/Example/Caller.cs", assemblyName);
        }

        private static string FormatWarning(string removedKey, params string[] callerKeys)
        {
            return string.Format(
                HotReloadConstants.StaleSignatureCallersWarningFormat,
                removedKey,
                string.Join(", ", callerKeys));
        }
    }
}
