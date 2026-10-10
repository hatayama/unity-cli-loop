using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage tests for signature-change caller identity and gate classification.
    /// </summary>
    public class HotReloadSignatureChangeCoverageTests
    {
        private const string EditedAssemblyName = "EditedAssembly";
        private const string ExternalAssemblyName = "ExternalAssembly";
        private const string CallerKey = "Example.Caller::Call()";
        private const string ReplacementKey = "Example.Target::Call()";

        /// <summary>
        /// What: a caller from another assembly with the same wire key as an edited caller stays uncovered.
        /// </summary>
        [Test]
        public void CollectUncoveredCallersByTarget_ExternalSameKeyCaller_StaysUncovered()
        {
            TransformWorkerEntryDto localCaller = CreateOrdinaryEntry();
            HotReloadCallSiteHit hit = CreateHit(ExternalAssemblyName);

            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncovered =
                HotReloadSignatureChangeGate.CollectInitialUncoveredCallers(
                    EditedAssemblyName,
                    new[] { localCaller },
                    new[] { hit },
                    new HashSet<HotReloadQualifiedMethodIdentity>());

            Assert.That(uncovered, Does.ContainKey(ReplacementKey));
            Assert.That(
                uncovered[ReplacementKey],
                Does.Contain(new HotReloadQualifiedMethodIdentity(ExternalAssemblyName, CallerKey)));
        }

        /// <summary>
        /// What: a caller with the same key in the edited assembly is covered by its worker entry.
        /// </summary>
        [Test]
        public void CollectInitialUncoveredCallers_SameAssemblySameKeyCaller_IsCovered()
        {
            TransformWorkerEntryDto localCaller = CreateOrdinaryEntry();
            HotReloadCallSiteHit hit = CreateHit(EditedAssemblyName);

            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncovered =
                HotReloadSignatureChangeGate.CollectInitialUncoveredCallers(
                    EditedAssemblyName,
                    new[] { localCaller },
                    new[] { hit },
                    new HashSet<HotReloadQualifiedMethodIdentity>());

            Assert.That(uncovered, Is.Empty);
        }

        /// <summary>
        /// What: an external caller sharing an edited-file method key receives the generic gate reason.
        /// </summary>
        [Test]
        public void BuildGatedReplacementSkipOutcomes_ExternalSameKeyCaller_UsesGenericReason()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry();
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredByTarget =
                new Dictionary<string, List<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal)
                {
                    {
                        ReplacementKey,
                        new List<HotReloadQualifiedMethodIdentity>
                        {
                            new HotReloadQualifiedMethodIdentity(ExternalAssemblyName, CallerKey)
                        }
                    }
                };
            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> editedFileIdentities =
                new Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal)
                {
                    {
                        replacement.sourceProjectRelativePath,
                        new HashSet<HotReloadQualifiedMethodIdentity>
                        {
                            new HotReloadQualifiedMethodIdentity(EditedAssemblyName, CallerKey)
                        }
                    }
                };
            HotReloadGroupFilePaths paths = HotReloadGroupFilePaths.ForSingleFile(
                replacement.sourceProjectRelativePath,
                "Assembly.dll");

            List<HotReloadMethodOutcome> outcomes =
                HotReloadSignatureChangeGate.BuildGatedReplacementSkipOutcomes(
                    HotReloadCompositionRoot.Services.Domain,
                    new[] { replacement },
                    uncoveredByTarget,
                    editedFileIdentities,
                    paths);

            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonFormat,
                HotReloadSignatureChangeGate.FormatGatedReplacementRegistryKey(replacement));
            Assert.That(outcomes, Has.Count.EqualTo(1));
            Assert.That(outcomes[0].Reason, Is.EqualTo(expectedReason));
        }

        /// <summary>
        /// What: a same-assembly caller listed by the edited file receives the same-file gate reason.
        /// </summary>
        [Test]
        public void BuildGatedReplacementSkipOutcomes_SameAssemblySameKeyCaller_UsesSameFileReason()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry();
            HotReloadQualifiedMethodIdentity localCallerIdentity =
                new HotReloadQualifiedMethodIdentity(EditedAssemblyName, CallerKey);
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> uncoveredByTarget =
                new Dictionary<string, List<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal)
                {
                    { ReplacementKey, new List<HotReloadQualifiedMethodIdentity> { localCallerIdentity } }
                };
            Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>> editedFileIdentities =
                new Dictionary<string, HashSet<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal)
                {
                    {
                        replacement.sourceProjectRelativePath,
                        new HashSet<HotReloadQualifiedMethodIdentity> { localCallerIdentity }
                    }
                };
            HotReloadGroupFilePaths paths = HotReloadGroupFilePaths.ForSingleFile(
                replacement.sourceProjectRelativePath,
                "Assembly.dll");

            List<HotReloadMethodOutcome> outcomes =
                HotReloadSignatureChangeGate.BuildGatedReplacementSkipOutcomes(
                    HotReloadCompositionRoot.Services.Domain,
                    new[] { replacement },
                    uncoveredByTarget,
                    editedFileIdentities,
                    paths);

            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonSameFileCallersFormat,
                HotReloadSignatureChangeGate.FormatGatedReplacementRegistryKey(replacement),
                "Caller.Call");
            Assert.That(outcomes, Has.Count.EqualTo(1));
            Assert.That(outcomes[0].Reason, Is.EqualTo(expectedReason));
        }

        /// <summary>
        /// What: final coverage rejects a replacement when only a same-key caller from another assembly was kept.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_ExternalSameKeyCallerRemains_ReturnsReplacementKey()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry();
            TransformWorkerEntryDto localCaller = CreateOrdinaryEntry();
            HotReloadCallSiteHit hit = CreateHit(ExternalAssemblyName);

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { replacement, localCaller },
                new[] { hit },
                new HashSet<HotReloadQualifiedMethodIdentity>());

            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// A caller replacement that disappears after the gate retry is not treated as a deletion.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_DroppedSourceLiveCaller_GatesRemainingReplacement()
        {
            TransformWorkerEntryDto callerReplacement = CreateOrdinaryEntry();
            callerReplacement.replacesCompiledMethod = true;
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { callerReplacement, targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// A caller replacement that remains in the final apply set covers its target.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_RetainedSourceLiveCaller_AllowsRemainingReplacement()
        {
            TransformWorkerEntryDto callerReplacement = CreateOrdinaryEntry();
            callerReplacement.replacesCompiledMethod = true;
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { callerReplacement, targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { callerReplacement, targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(losses, Is.Empty);
        }

        /// <summary>
        /// An unchanged initial caller is source-live and cannot become a deletion exemption.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_UnchangedSourceLiveCaller_GatesRemainingReplacement()
        {
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateCallerUnchangedMethod() },
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(exemptions, Is.Empty);
            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// A caller absent from the initial source-live rows remains a deletion exemption.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_DeletedCaller_AllowsRemainingReplacement()
        {
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(losses, Is.Empty);
        }

        /// <summary>
        /// A deleted local caller does not exempt a same-key caller from another assembly.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_ExternalSameKeyCaller_IsNotDeletionExempt()
        {
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                Array.Empty<TransformWorkerSkippedDto>(),
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(ExternalAssemblyName) },
                exemptions);

            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// An unidentified initial skipped row removes every deletion exemption.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_UnknownSkippedMethodKey_GatesRemainingReplacement()
        {
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                new[] { new TransformWorkerSkippedDto() },
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(exemptions, Is.Empty);
            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// A keyed skipped caller remains source-live and is not a deletion exemption.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_KeyedSkippedCaller_GatesRemainingReplacement()
        {
            TransformWorkerEntryDto targetReplacement = CreateReplacementEntry();
            HashSet<HotReloadQualifiedMethodIdentity> exemptions = HotReloadDeletedCallerExemptions.Collect(
                EditedAssemblyName,
                new[] { targetReplacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>(),
                new[] { new TransformWorkerSkippedDto { methodKey = CallerKey } },
                new[] { CreateCallerRemovedSignature() });

            List<string> losses = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                EditedAssemblyName,
                new[] { targetReplacement },
                new[] { CreateHit(EditedAssemblyName) },
                exemptions);

            Assert.That(exemptions, Is.Empty);
            Assert.That(losses, Is.EqualTo(new[] { ReplacementKey }));
        }

        /// <summary>
        /// What: a foreign same-key caller does not produce an already-patched caller notice.
        /// </summary>
        [Test]
        public void AppendSignatureChangeCallersRepatchedWarnings_ExternalSameKeyCaller_OmitsNotice()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry();
            TransformWorkerEntryDto localCaller = CreateOrdinaryEntry();
            HotReloadCallSiteHit hit = CreateHit(ExternalAssemblyName);
            List<string> warnings = new List<string>();
            HashSet<string> snapshotLabels = new HashSet<string>(StringComparer.Ordinal)
            {
                HotReloadMethodKeys.FormatMethodLabelParts(
                    new HotReloadMetadataTypeName("Example.Caller"),
                    "Call",
                    Array.Empty<string>(),
                    0)
            };

            HotReloadSignatureChangeCoverage.AppendSignatureChangeCallersRepatchedWarnings(
                warnings,
                EditedAssemblyName,
                new[] { replacement, localCaller },
                new[] { hit },
                snapshotLabels);

            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// What: the stale call sites of a removed signature keep the first hit of each uncovered
        /// caller identity, so callers with the same wire key in two assemblies both reach the
        /// run-end filter, and a removed signature without an uncovered caller records nothing.
        /// </summary>
        [Test]
        public void CollectStaleSignatureCallSites_CrossAssemblySameKey_KeepsFirstHitPerIdentity()
        {
            HotReloadCallSiteHit editedHit = CreateHit(EditedAssemblyName);
            HotReloadCallSiteHit repeatedEditedHit = CreateHit(EditedAssemblyName);
            HotReloadCallSiteHit externalHit = CreateHit(ExternalAssemblyName);
            List<HotReloadCallSiteHit> hits =
                new List<HotReloadCallSiteHit> { editedHit, repeatedEditedHit, externalHit };
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> callersByTarget =
                HotReloadSignatureChangeCoverage.CollectUncoveredCallersByTarget(
                    hits,
                    new HashSet<HotReloadQualifiedMethodIdentity>());

            List<HotReloadStaleSignatureCallSites> callSites =
                HotReloadSignatureChangeCoverage.CollectStaleSignatureCallSites(
                    new[] { CreateTargetRemovedSignature("Call"), CreateTargetRemovedSignature("Uncalled") },
                    hits,
                    callersByTarget);

            Assert.That(callSites, Has.Count.EqualTo(1));
            Assert.That(callSites[0].RemovedMethodKey, Is.EqualTo(ReplacementKey));
            Assert.That(callSites[0].Callers, Is.EqualTo(new[] { editedHit, externalHit }));
        }

        private static TransformWorkerEntryDto CreateReplacementEntry()
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Fixture.cs",
                typeMetadataName = "Example.Target",
                methodName = "Call",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0,
                replacesCompiledMethod = true
            };
        }

        private static TransformWorkerRemovedMethodSignatureDto CreateCallerRemovedSignature()
        {
            return new TransformWorkerRemovedMethodSignatureDto
            {
                typeMetadataName = "Example.Caller",
                methodName = "Call",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0
            };
        }

        private static TransformWorkerUnchangedMethodDto CreateCallerUnchangedMethod()
        {
            return new TransformWorkerUnchangedMethodDto
            {
                sourceProjectRelativePath = "Assets/Fixture.cs",
                typeMetadataName = "Example.Caller",
                methodName = "Call",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0
            };
        }

        private static TransformWorkerRemovedMethodSignatureDto CreateTargetRemovedSignature(string methodName)
        {
            return new TransformWorkerRemovedMethodSignatureDto
            {
                typeMetadataName = "Example.Target",
                methodName = methodName,
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0
            };
        }

        private static TransformWorkerEntryDto CreateOrdinaryEntry()
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = "Assets/Fixture.cs",
                typeMetadataName = "Example.Caller",
                methodName = "Call",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0
            };
        }

        private static HotReloadCallSiteHit CreateHit(string callerAssemblyName)
        {
            return new HotReloadCallSiteHit
            {
                CallerAssemblyName = callerAssemblyName,
                CallerTypeMetadataName = new HotReloadMetadataTypeName("Example.Caller"),
                CallerMethodName = "Call",
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerMethodKey = CallerKey,
                CallerGenericArity = 0,
                TargetMethodKey = ReplacementKey
            };
        }

        /// <summary>
        /// What: a worker-shaped label spells a nested caller type the way reflection does, so a
        /// row built before Resolve matches the label a resolved MethodBase would produce.
        /// </summary>
        [Test]
        public void FormatMethodLabelParts_WithNestedMetadataTypeName_UsesTheReflectionSeparator()
        {
            string label = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName("Example.Outer/Inner"),
                "Call",
                new[] { "Example.Outer/Argument" },
                0);

            Assert.That(label, Is.EqualTo("Example.Outer+Inner.Call(Example.Outer+Argument)"));
        }

        /// <summary>
        /// What: the short caller name of a nested type keeps only the innermost type, because the
        /// display form makes a nesting step indistinguishable from a namespace segment.
        /// </summary>
        [Test]
        public void FormatCallerShortName_WithNestedMetadataTypeName_KeepsTheInnermostType()
        {
            string shortName = HotReloadSignatureChangeCoverage.FormatCallerShortName(
                "Example.Outer/Inner::Call()");

            Assert.That(shortName, Is.EqualTo("Inner.Call"));
        }

    }
}
