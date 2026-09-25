using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers which files of an assembly a reload pulls back in: active files that rebind against
    /// the new shim (in particular a declaration file the last compile never listed), files left
    /// Skipped or Failed, and companion files.
    /// </summary>
    public class HotReloadActiveSiblingRebindPlannerTests
    {
        private const string AssemblyName = "SiblingPlannerFixtureAssembly";
        private const string OtherAssemblyName = "SiblingPlannerOtherAssembly";
        private const string CompiledSiblingPath = "Assets/Tests/Editor/HotReload/PlannerCompiledSibling.cs";
        private const string NewSourcePath = "Assets/Tests/Editor/HotReload/PlannerNewSource.cs";
        private const string OtherOwnerPath = "Assets/Tests/Editor/HotReload/PlannerOtherOwner.cs";

        private HotReloadDomainTestScope _scope;
        private HotReloadDomainTestAccess _access;
        private Dictionary<string, string> _workerSourceByPath;
        private string _temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _access = new HotReloadDomainTestAccess();
            _workerSourceByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            _temporaryDirectory = Path.Combine(Path.GetTempPath(), "uloop-sibling-planner-" + Guid.NewGuid());
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        /// <summary>
        /// What: a file the compiled source list does not name is pulled back in when the domain
        /// still holds the evidence that it belongs to this assembly. That is the only file kind
        /// hot reload can introduce a type from, and without this it is left behind on every
        /// reload that does not name it.
        /// </summary>
        [Test]
        public void Plan_NewSourceOfThisAssemblyWithEvidence_IsIncluded()
        {
            ArrangeAppliedFile(NewSourcePath);
            RecordEvidence(NewSourcePath, AssemblyName);

            HotReloadActiveSiblingRebindPlan plan = Plan(Array.Empty<string>());

            Assert.That(PathsOf(plan), Is.EqualTo(new[] { NewSourcePath }));
        }

        /// <summary>
        /// What: the same file without evidence stays out. Unity does not vouch for the assembly
        /// of a file it never compiled, so a guess here would patch it into the wrong assembly.
        /// </summary>
        [Test]
        public void Plan_NewSourceWithoutEvidence_IsNotIncluded()
        {
            ArrangeAppliedFile(NewSourcePath);

            HotReloadActiveSiblingRebindPlan plan = Plan(Array.Empty<string>());

            Assert.That(PathsOf(plan), Is.Empty);
        }

        /// <summary>
        /// What: evidence naming a different assembly does not let the file in, so the sibling
        /// sweep of one assembly never drags in a new source proved to belong to another.
        /// </summary>
        [Test]
        public void Plan_NewSourceWhoseEvidenceNamesAnotherAssembly_IsNotIncluded()
        {
            ArrangeAppliedFile(NewSourcePath);
            RecordEvidence(NewSourcePath, OtherAssemblyName);

            HotReloadActiveSiblingRebindPlan plan = Plan(Array.Empty<string>());

            Assert.That(PathsOf(plan), Is.Empty);
        }

        /// <summary>
        /// What: the declaring file of an introduced type is a candidate on the strength of the
        /// type alone. Such a file holds no patch and no added member of its own, so the two
        /// existing candidate sources never name it.
        /// </summary>
        [Test]
        public void Plan_IntroducedTypeOwnerWithNoPatchOfItsOwn_IsACandidate()
        {
            RegisterIntroducedTypes();
            RecordAppliedSourceOnly(NewSourcePath);
            RecordEvidence(NewSourcePath, AssemblyName);
            RecordAppliedSourceOnly(OtherOwnerPath);
            RecordEvidence(OtherOwnerPath, OtherAssemblyName);

            HotReloadActiveSiblingRebindPlan plan = Plan(Array.Empty<string>());

            Assert.That(PathsOf(plan), Is.EqualTo(new[] { NewSourcePath }));
        }

        /// <summary>
        /// What: a file the compiled source list does name is still pulled back in with no
        /// evidence at all, because Unity itself answers for its assembly.
        /// </summary>
        [Test]
        public void Plan_CompiledSiblingWithoutEvidence_IsStillIncluded()
        {
            ArrangeAppliedFile(CompiledSiblingPath);

            HotReloadActiveSiblingRebindPlan plan = Plan(new[] { CompiledSiblingPath });

            Assert.That(PathsOf(plan), Is.EqualTo(new[] { CompiledSiblingPath }));
        }

        /// <summary>
        /// What: a new source whose text changed since it was applied is reported as changed
        /// rather than re-applied, the same way a compiled sibling is.
        /// </summary>
        [Test]
        public void Plan_NewSourceChangedSinceItWasApplied_IsReportedAsChanged()
        {
            ArrangeAppliedFile(NewSourcePath);
            RecordEvidence(NewSourcePath, AssemblyName);
            File.WriteAllText(_workerSourceByPath[NewSourcePath], "// edited after it was applied\n");

            HotReloadActiveSiblingRebindPlan plan = Plan(Array.Empty<string>());

            Assert.That(PathsOf(plan), Is.Empty);
            Assert.That(plan.ChangedSinceApplyPaths, Is.EqualTo(new[] { NewSourcePath }));
        }

        /// <summary>
        /// What: a file whose last reload left it Skipped or Failed and that holds nothing active
        /// comes back as a retry, the one candidate source that names such a file.
        /// </summary>
        [Test]
        public void Plan_NotFullyAppliedFileWithNothingActive_IsIncludedAsARetry()
        {
            RecordAppliedSourceOnly(CompiledSiblingPath, isFullyApplied: false);

            HotReloadActiveSiblingRebindPlan plan = Plan(new[] { CompiledSiblingPath });

            Assert.That(PathsOf(plan), Is.EqualTo(new[] { CompiledSiblingPath }));
            Assert.That(plan.FilesToInclude[0].Reason, Is.EqualTo(HotReloadSiblingInclusionReason.RetryAfterSkip));
        }

        /// <summary>
        /// What: a companion that holds active changes of its own comes back once, for those
        /// changes, and its stale companion hash raises no changed-companion warning.
        /// </summary>
        [Test]
        public void Plan_CompanionThatHoldsActiveChanges_IsIncludedOnceForItsChangesWithoutAWarning()
        {
            ArrangeAppliedFile(CompiledSiblingPath);
            _access.Domain.CompanionSources.Record(CompiledSiblingPath, "stale-companion-hash");

            HotReloadActiveSiblingRebindPlan plan = Plan(new[] { CompiledSiblingPath });

            Assert.That(PathsOf(plan), Is.EqualTo(new[] { CompiledSiblingPath }));
            Assert.That(plan.FilesToInclude[0].Reason, Is.EqualTo(HotReloadSiblingInclusionReason.ActiveChanges));
            Assert.That(plan.ChangedCompanionPaths, Is.Empty);
        }

        /// <summary>
        /// What: a companion is brought back while its bytes match the hash it was given at, and
        /// reported as changed once they differ.
        /// </summary>
        [Test]
        public void Plan_Companion_IsIncludedWhileUnchangedAndReportedOnceChanged()
        {
            string workerSourcePath = WriteWorkerSource(CompiledSiblingPath);
            _access.Domain.CompanionSources.Record(
                CompiledSiblingPath,
                new HotReloadSourceContentHasher().ComputeContentHash(File.ReadAllBytes(workerSourcePath)));

            HotReloadActiveSiblingRebindPlan unchanged = Plan(new[] { CompiledSiblingPath });
            File.WriteAllText(workerSourcePath, "// edited after it was given\n");
            HotReloadActiveSiblingRebindPlan changed = Plan(new[] { CompiledSiblingPath });

            Assert.That(PathsOf(unchanged), Is.EqualTo(new[] { CompiledSiblingPath }));
            Assert.That(unchanged.FilesToInclude[0].Reason, Is.EqualTo(HotReloadSiblingInclusionReason.Companion));
            Assert.That(PathsOf(changed), Is.Empty);
            Assert.That(changed.ChangedCompanionPaths, Is.EqualTo(new[] { CompiledSiblingPath }));
        }

        private HotReloadActiveSiblingRebindPlan Plan(string[] assemblySourceFiles)
        {
            return HotReloadActiveSiblingRebindPlanner.Plan(
                _access.Domain,
                AssemblyName,
                assemblySourceFiles,
                Array.Empty<string>(),
                path => _workerSourceByPath.TryGetValue(path, out string workerSourcePath)
                    ? workerSourcePath
                    : null);
        }

        private static string[] PathsOf(HotReloadActiveSiblingRebindPlan plan)
        {
            List<string> paths = new List<string>();
            for (int index = 0; index < plan.FilesToInclude.Count; index++)
            {
                paths.Add(plan.FilesToInclude[index].ProjectRelativePath);
            }

            return paths.ToArray();
        }

        // An applied file with an added member of its own: what makes the domain name it as an
        // active sibling in the first place.
        private void ArrangeAppliedFile(string projectRelativePath)
        {
            _access.RegisterAddedMember(
                projectRelativePath,
                "PlannerHost.Added" + Path.GetFileNameWithoutExtension(projectRelativePath) + "()",
                GetAddedTarget(),
                projectRelativePath);
            RecordAppliedSourceOnly(projectRelativePath);
        }

        private void RecordAppliedSourceOnly(string projectRelativePath, bool isFullyApplied = true)
        {
            string workerSourcePath = WriteWorkerSource(projectRelativePath);
            _access.Domain.AppliedSources.RecordAppliedSource(
                projectRelativePath,
                new HotReloadSourceContentHasher().ComputeContentHash(File.ReadAllBytes(workerSourcePath)),
                isFullyApplied);
        }

        private string WriteWorkerSource(string projectRelativePath)
        {
            string workerSourcePath = Path.Combine(
                _temporaryDirectory,
                Path.GetFileName(projectRelativePath));
            File.WriteAllText(workerSourcePath, "// " + projectRelativePath + "\n");
            _workerSourceByPath[projectRelativePath] = workerSourcePath;
            return workerSourcePath;
        }

        private void RecordEvidence(string projectRelativePath, string assemblyName)
        {
            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(
                projectRelativePath,
                new HotReloadNewSourceMembershipEvidence(
                    projectRelativePath,
                    assemblyName,
                    Path.Combine("Library", "ScriptAssemblies", assemblyName + ".dll"),
                    "planner-mvid",
                    null,
                    Array.Empty<HotReloadNewSourceMembershipBoundary>()));
        }

        // Two active introduced types owned by different assemblies, so the sweep of one assembly
        // is seen to leave the other's declaration file alone.
        private void RegisterIntroducedTypes()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadActiveSiblingRebindPlannerTests).Assembly,
                "planner-artifact.dll",
                "planner-artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateDescriptor(AssemblyName, NewSourcePath, "Planner.Introduced"),
                    CreateDescriptor(OtherAssemblyName, OtherOwnerPath, "Planner.OtherIntroduced")
                });
            _access.Domain.IntroducedTypes.RegisterPrepared(artifact);
            _access.Domain.IntroducedTypes.Activate(artifact);
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(
            string originalAssemblyName,
            string ownerProjectRelativePath,
            string metadataName)
        {
            return new HotReloadIntroducedTypeDescriptor(
                originalAssemblyName,
                "planner-original-mvid",
                metadataName,
                ownerProjectRelativePath,
                "planner-fingerprint-" + metadataName,
                "public class Introduced { }");
        }

        private static MethodInfo GetAddedTarget()
        {
            return typeof(HotReloadActiveSiblingRebindPlannerTests).GetMethod(
                nameof(AddedTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static void AddedTarget()
        {
        }
    }
}
