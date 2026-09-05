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
            HotReloadCallSiteScanner.CallSiteHit hit = CreateHit(ExternalAssemblyName);

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
            HotReloadCallSiteScanner.CallSiteHit hit = CreateHit(EditedAssemblyName);

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
            HotReloadCallSiteScanner.CallSiteHit hit = CreateHit(ExternalAssemblyName);

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
            HotReloadCallSiteScanner.CallSiteHit hit = CreateHit(ExternalAssemblyName);
            List<string> warnings = new List<string>();
            HashSet<string> snapshotLabels = new HashSet<string>(StringComparer.Ordinal)
            {
                HotReloadMethodKeys.FormatMethodLabelParts(
                    "Example.Caller",
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
        /// What: stale warnings de-duplicate only their displayed caller keys, preserving distinct
        /// cross-assembly caller identities for coverage decisions.
        /// </summary>
        [Test]
        public void FormatUncoveredCallerMethodKeys_CrossAssemblySameKey_DeduplicatesDisplayOnly()
        {
            List<HotReloadQualifiedMethodIdentity> sameKeyCallers =
                new List<HotReloadQualifiedMethodIdentity>
                {
                    new HotReloadQualifiedMethodIdentity(EditedAssemblyName, CallerKey),
                    new HotReloadQualifiedMethodIdentity(ExternalAssemblyName, CallerKey)
                };
            List<HotReloadQualifiedMethodIdentity> differentKeyCallers =
                new List<HotReloadQualifiedMethodIdentity>
                {
                    new HotReloadQualifiedMethodIdentity(EditedAssemblyName, CallerKey),
                    new HotReloadQualifiedMethodIdentity(
                        ExternalAssemblyName,
                        "Example.OtherCaller::Call()")
                };

            Assert.That(sameKeyCallers, Has.Count.EqualTo(2));
            Assert.That(
                CountOccurrences(CollectStaleWarning(sameKeyCallers), CallerKey),
                Is.EqualTo(1));
            Assert.That(
                CollectStaleWarning(differentKeyCallers),
                Does.Contain("Example.Caller::Call(), Example.OtherCaller::Call()"));
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

        private static string CollectStaleWarning(
            List<HotReloadQualifiedMethodIdentity> callers)
        {
            TransformWorkerRemovedMethodSignatureDto removedSignature =
                new TransformWorkerRemovedMethodSignatureDto
                {
                    typeMetadataName = "Example.Target",
                    methodName = "Call",
                    parameterTypeFullNames = Array.Empty<string>(),
                    genericArity = 0
                };
            Dictionary<string, List<HotReloadQualifiedMethodIdentity>> callersByTarget =
                new Dictionary<string, List<HotReloadQualifiedMethodIdentity>>(StringComparer.Ordinal)
                {
                    { ReplacementKey, callers }
                };

            List<string> warnings = HotReloadSignatureChangeCoverage.CollectStaleSignatureWarnings(
                new[] { removedSignature },
                callersByTarget);

            return warnings[0];
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int startIndex = 0;
            while (true)
            {
                int occurrenceIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal);
                if (occurrenceIndex < 0)
                {
                    return count;
                }

                count++;
                startIndex = occurrenceIndex + value.Length;
            }
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

        private static HotReloadCallSiteScanner.CallSiteHit CreateHit(string callerAssemblyName)
        {
            return new HotReloadCallSiteScanner.CallSiteHit
            {
                CallerAssemblyName = callerAssemblyName,
                CallerTypeMetadataName = "Example.Caller",
                CallerMethodName = "Call",
                CallerParameterTypeFullNames = Array.Empty<string>(),
                CallerMethodKey = CallerKey,
                CallerGenericArity = 0,
                TargetMethodKey = ReplacementKey
            };
        }

    }
}
