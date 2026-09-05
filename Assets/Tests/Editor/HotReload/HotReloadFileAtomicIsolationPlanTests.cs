using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the two exclusion branches a failed group shim compile chooses
    /// between: keep the narrow method-and-caller exclusion when no file survives, and drop whole
    /// files when at least one file can still be applied.
    /// </summary>
    public class HotReloadFileAtomicIsolationPlanTests
    {
        private const string FirstFile = "Assets/Scripts/First.cs";
        private const string SecondFile = "Assets/Scripts/Second.cs";

        /// <summary>
        /// What: when every file of the group failed, the plan keeps the legacy exclusion set —
        /// the failed added method plus the entries that call it — and leaves the file's other
        /// entries in, instead of excluding the whole file.
        /// </summary>
        [Test]
        public void Build_WhenEveryFileFailed_ExcludesOnlyTheFailedMethodAndItsCallers()
        {
            TransformWorkerEntryDto brokenAddedMethod = CreateEntry(
                FirstFile,
                "AddedHelper",
                HotReloadConstants.PatchKindAddedMethod);
            TransformWorkerEntryDto caller = CreateEntry(
                FirstFile,
                "CallsAddedHelper",
                HotReloadConstants.PatchKindDelegation);
            caller.calledAddedMethodKeys = new[] { HotReloadMethodKeys.BuildMethodKey(brokenAddedMethod) };
            TransformWorkerEntryDto unrelated = CreateEntry(
                FirstFile,
                "Unrelated",
                HotReloadConstants.PatchKindDelegation);
            TransformWorkerEntryDto[] entries = new[] { brokenAddedMethod, caller, unrelated };

            HotReloadFileAtomicIsolationPlan plan = HotReloadFileAtomicIsolationPlan.Build(
                entries,
                CreateAttribution(brokenAddedMethod),
                Array.Empty<TransformWorkerSkippedDto>(),
                CreateGroupFilePaths(FirstFile),
                new[] { FirstFile });

            Assert.That(plan.AllFilesFailed, Is.True);
            Assert.That(plan.IsFailedFile(FirstFile), Is.True);
            Assert.That(
                plan.ExcludedAddedMethodKeys,
                Is.EqualTo(new[] { HotReloadMethodKeys.BuildMethodKey(brokenAddedMethod) }));
            Assert.That(
                plan.ExcludedMethodKeys,
                Is.EqualTo(new[] { HotReloadMethodKeys.BuildMethodKey(caller) }));
            Assert.That(plan.CallerEntries, Is.EqualTo(new[] { caller }));
            Assert.That(plan.AtomicSkipOutcomesByFile, Is.Empty);
            Assert.That(
                HotReloadFileAtomicIsolationPlan.CollectOutcomes(plan.FailedOutcomesByFile, new[] { FirstFile }),
                Has.Count.EqualTo(1));
        }

        /// <summary>
        /// What: when one file of the group survived, the plan excludes every entry of the failed
        /// file (added methods through the added-method set), reports the failed file's remaining
        /// entries as file-atomic skips, and touches nothing of the surviving file.
        /// </summary>
        [Test]
        public void Build_WhenOneFileSurvived_ExcludesEveryEntryOfTheFailedFile()
        {
            TransformWorkerEntryDto brokenBody = CreateEntry(
                FirstFile,
                "BrokenBody",
                HotReloadConstants.PatchKindDelegation);
            TransformWorkerEntryDto sameFileAddedMethod = CreateEntry(
                FirstFile,
                "AddedHelper",
                HotReloadConstants.PatchKindAddedMethod);
            TransformWorkerEntryDto sameFileHealthy = CreateEntry(
                FirstFile,
                "Healthy",
                HotReloadConstants.PatchKindDelegation);
            TransformWorkerEntryDto survivingFileEntry = CreateEntry(
                SecondFile,
                "SurvivingBody",
                HotReloadConstants.PatchKindDelegation);
            TransformWorkerEntryDto[] entries =
                new[] { brokenBody, sameFileAddedMethod, sameFileHealthy, survivingFileEntry };

            HotReloadFileAtomicIsolationPlan plan = HotReloadFileAtomicIsolationPlan.Build(
                entries,
                CreateAttribution(brokenBody),
                Array.Empty<TransformWorkerSkippedDto>(),
                CreateGroupFilePaths(FirstFile, SecondFile),
                new[] { FirstFile, SecondFile });

            Assert.That(plan.AllFilesFailed, Is.False);
            Assert.That(plan.IsFailedFile(FirstFile), Is.True);
            Assert.That(plan.IsFailedFile(SecondFile), Is.False);
            Assert.That(
                plan.ExcludedMethodKeys,
                Is.EquivalentTo(new[]
                {
                    HotReloadMethodKeys.BuildMethodKey(brokenBody),
                    HotReloadMethodKeys.BuildMethodKey(sameFileHealthy)
                }));
            Assert.That(
                plan.ExcludedAddedMethodKeys,
                Is.EqualTo(new[] { HotReloadMethodKeys.BuildMethodKey(sameFileAddedMethod) }));
            Assert.That(
                plan.ExcludedMethodKeys,
                Does.Not.Contain(HotReloadMethodKeys.BuildMethodKey(survivingFileEntry)));
            Assert.That(plan.CallerEntries, Is.Empty);

            List<HotReloadMethodOutcome> atomicSkips = HotReloadFileAtomicIsolationPlan.CollectOutcomes(
                plan.AtomicSkipOutcomesByFile,
                new[] { FirstFile, SecondFile });
            Assert.That(atomicSkips, Has.Count.EqualTo(2));
            foreach (HotReloadMethodOutcome outcome in atomicSkips)
            {
                Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped));
                Assert.That(outcome.Reason, Is.EqualTo(HotReloadConstants.AtomicFileSkipReason));
                Assert.That(outcome.FilePath, Is.EqualTo(FirstFile));
            }
        }

        private static TransformWorkerEntryDto CreateEntry(
            string projectRelativePath,
            string methodName,
            string patchKind)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = projectRelativePath,
                typeMetadataName = "Sample.Host",
                methodName = methodName,
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0,
                patchKind = patchKind
            };
        }

        private static HotReloadShimErrorAttribution.ShimCompileErrorAttribution CreateAttribution(
            TransformWorkerEntryDto failedEntry)
        {
            Dictionary<TransformWorkerEntryDto, List<string>> errorMessagesByEntry =
                new Dictionary<TransformWorkerEntryDto, List<string>>
                {
                    { failedEntry, new List<string> { "CS0103: name not found (line 12)" } }
                };
            return new HotReloadShimErrorAttribution.ShimCompileErrorAttribution(errorMessagesByEntry);
        }

        private static HotReloadGroupFilePaths CreateGroupFilePaths(params string[] projectRelativePaths)
        {
            List<(string ProjectRelativePath, string AssemblyResolvePath)> files =
                new List<(string ProjectRelativePath, string AssemblyResolvePath)>();
            foreach (string projectRelativePath in projectRelativePaths)
            {
                files.Add((projectRelativePath, projectRelativePath));
            }

            return new HotReloadGroupFilePaths(files);
        }
    }
}
