using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Which closing instruction a deactivation warning carries. A member this run also reported
    /// as Skipped comes back only once the shape its reason names changes, so it must not be told
    /// to simply reload again, and a run that deactivated both kinds must not give either group
    /// the other's instruction.
    /// </summary>
    public class HotReloadDeactivatedWarningWordingTests
    {
        private const string ProjectRelativePath = "Assets/Example/Deactivated.cs";
        private const string TypeMetadataName = "Example.Host";
        private const string SkippedMethodName = "SkippedMember";
        private const string DeactivatedMethodName = "DeactivatedMember";

        /// <summary>
        /// What: a deactivated added member this run also reported as Skipped is warned about with
        /// the wording that sends the reader to its reason instead of to another reload.
        /// </summary>
        [Test]
        public void AppendDeactivatedPatchesWarning_AddedMemberSkippedByThisRun_NamesItsReason()
        {
            List<string> warnings = WarnAddedMembers(
                new[] { SkippedMethodName },
                addedLabels: new[] { SkippedMethodName },
                skippedMethodNames: new[] { SkippedMethodName });

            Assert.That(
                warnings,
                Is.EqualTo(new[] { Expected(HotReloadConstants.DeactivatedSkippedAddedMembersWarningFormat, SkippedMethodName) }));
        }

        /// <summary>
        /// What: a deactivated added member this run did not skip keeps the wording that invites
        /// another reload, because another reload is what re-applies it.
        /// </summary>
        [Test]
        public void AppendDeactivatedPatchesWarning_AddedMemberNotSkippedByThisRun_InvitesAnotherReload()
        {
            List<string> warnings = WarnAddedMembers(
                new[] { DeactivatedMethodName },
                addedLabels: new[] { DeactivatedMethodName },
                skippedMethodNames: new string[0]);

            Assert.That(
                warnings,
                Is.EqualTo(new[] { Expected(HotReloadConstants.DeactivatedAddedMembersWarningFormat, DeactivatedMethodName) }));
        }

        /// <summary>
        /// What: a run that deactivated one member of each kind reports them in two warnings, so
        /// neither group carries the instruction that belongs to the other.
        /// </summary>
        [Test]
        public void AppendDeactivatedPatchesWarning_BothKindsInOneRun_ReportsThemSeparately()
        {
            List<string> warnings = WarnAddedMembers(
                new[] { SkippedMethodName, DeactivatedMethodName },
                addedLabels: new[] { SkippedMethodName, DeactivatedMethodName },
                skippedMethodNames: new[] { SkippedMethodName });

            Assert.That(
                warnings,
                Is.EqualTo(
                    new[]
                    {
                        Expected(HotReloadConstants.DeactivatedAddedMembersWarningFormat, DeactivatedMethodName),
                        Expected(HotReloadConstants.DeactivatedSkippedAddedMembersWarningFormat, SkippedMethodName)
                    }));
        }

        /// <summary>
        /// What: the same split applies to a deactivated patch of a compiled method, which carries
        /// the patch wording rather than the added-member one.
        /// </summary>
        [Test]
        public void AppendDeactivatedPatchesWarning_PatchesOfBothKinds_UseThePatchWordings()
        {
            List<string> warnings = Warn(
                new[] { SkippedMethodName, DeactivatedMethodName },
                addedLabels: new[] { DeactivatedMethodName },
                skippedMethodNames: new[] { SkippedMethodName },
                snapshotAddedMethodNames: new string[0]);

            Assert.That(
                warnings,
                Is.EqualTo(
                    new[]
                    {
                        Expected(HotReloadConstants.DeactivatedPatchesWarningFormat, DeactivatedMethodName),
                        Expected(HotReloadConstants.DeactivatedSkippedPatchesWarningFormat, SkippedMethodName)
                    }));
        }

        // The snapshot members are treated as added members unless the caller says otherwise,
        // because that is the shape most of these runs report.
        private static List<string> WarnAddedMembers(
            string[] snapshotMethodNames,
            string[] addedLabels,
            string[] skippedMethodNames)
        {
            return Warn(snapshotMethodNames, addedLabels, skippedMethodNames, snapshotMethodNames);
        }

        // The domain is empty, so every snapshot label reads as no longer active - which is the
        // state this warning describes.
        private static List<string> Warn(
            string[] snapshotMethodNames,
            string[] addedLabels,
            string[] skippedMethodNames,
            string[] snapshotAddedMethodNames)
        {
            List<string> warnings = new List<string>();
            HotReloadAppliedSourceLifecycle.AppendDeactivatedPatchesWarning(
                HotReloadCompositionRoot.CreateProductionDomain(),
                warnings,
                Labels(snapshotMethodNames),
                Labels(snapshotAddedMethodNames),
                Labels(new string[0]),
                ProjectRelativePath,
                BuildWorkerOutput(addedLabels),
                BuildOutcomes(skippedMethodNames));
            return warnings;
        }

        private static HashSet<string> Labels(string[] methodNames)
        {
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (string methodName in methodNames)
            {
                labels.Add(Label(methodName));
            }

            return labels;
        }

        private static string Label(string methodName)
        {
            return HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(TypeMetadataName),
                methodName,
                new string[0],
                0);
        }

        private static string Expected(string format, string methodName)
        {
            return string.Format(format, Label(methodName));
        }

        private static TransformWorkerOutputDto BuildWorkerOutput(string[] addedMethodNames)
        {
            List<TransformWorkerEntryDto> entries = new List<TransformWorkerEntryDto>();
            foreach (string methodName in addedMethodNames)
            {
                entries.Add(new TransformWorkerEntryDto
                {
                    sourceProjectRelativePath = ProjectRelativePath,
                    typeMetadataName = TypeMetadataName,
                    methodName = methodName,
                    parameterTypeFullNames = new string[0],
                    genericArity = 0,
                    patchKind = HotReloadConstants.PatchKindAddedMethod
                });
            }

            return new TransformWorkerOutputDto { entries = entries.ToArray() };
        }

        private static List<HotReloadMethodOutcome> BuildOutcomes(string[] skippedMethodNames)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            foreach (string methodName in skippedMethodNames)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(Label(methodName), "reason", ProjectRelativePath));
            }

            return outcomes;
        }
    }
}
