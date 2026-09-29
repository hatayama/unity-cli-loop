using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the check that holds a prepared artifact back until this run patches
    /// every body the artifact stubs: which files count as patching a body, and which rows the
    /// artifact's types get when one stub is left in place.
    /// </summary>
    public class HotReloadStubbedMemberCoverageTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string OwnerPath = "Assets/StubbedCaller.cs";
        private const string SiblingOwnerPath = "Assets/StubbedSibling.cs";
        private const string CallerMetadataName = "Example.StubbedCaller";
        private const string SiblingMetadataName = "Example.StubbedSibling";
        private const string UsesKey = "Example.StubbedCaller::Uses()";
        private const string GetterKey = "Example.StubbedCaller::get_Value()";
        private const string NestedParameterMetadataName = "Example.Host/Inner";
        private const string NestedParameterKey = "Example.StubbedCaller::Uses(" + NestedParameterMetadataName + ")";

        /// <summary>
        /// What: an artifact whose every stub has an entry in a resolved file may be activated.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_EveryStubResolved_ReturnsNoRows()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { UsesKey, GetterKey });
            HotReloadPreparedGroupFile resolved = CreateResolved(
                CreateEntry("Uses"),
                CreateEntry("get_Value"),
                CreateEntry("Unrelated"));

            List<HotReloadIntroducedTypeOutcome> rows =
                HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, new[] { resolved });

            Assert.That(rows, Is.Empty);
        }

        /// <summary>
        /// What: one stub without an entry holds the whole artifact back: the stubbed type's row
        /// names the method left unpatched, and the sibling type fails with it.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_OneStubUnresolved_FailsEveryTypeOfTheArtifact()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { UsesKey, GetterKey });
            HotReloadPreparedGroupFile resolved = CreateResolved(CreateEntry("Uses"));

            List<HotReloadIntroducedTypeOutcome> rows =
                HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, new[] { resolved });

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Kind, Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed));
            Assert.That(rows[0].MetadataName, Is.EqualTo(CallerMetadataName));
            Assert.That(rows[0].OwnerProjectRelativePath, Is.EqualTo(OwnerPath));
            Assert.That(
                rows[0].Reason,
                Does.StartWith("Not introduced: Example.StubbedCaller.get_Value() calls members that a hot reload added"));
            Assert.That(rows[0].Reason, Does.Not.Contain("Uses"));
            Assert.That(rows[1].Kind, Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed));
            Assert.That(rows[1].MetadataName, Is.EqualTo(SiblingMetadataName));
            Assert.That(rows[1].Reason, Does.Contain("another declaration in the same introduced-type batch"));
        }

        /// <summary>
        /// What: an entry of a file the group skipped patches nothing, so it does not cover a stub.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_StubOnlyInASkippedFile_FailsTheArtifact()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { UsesKey });
            HotReloadPreparedGroupFile skipped = HotReloadPreparedGroupFile.SkippedByGroup(CreateFile(OwnerPath));
            HotReloadPreparedGroupFile noEntries = HotReloadPreparedGroupFile.NoEntriesToApply(CreateFile(SiblingOwnerPath));

            List<HotReloadIntroducedTypeOutcome> rows =
                HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, new[] { skipped, noEntries });

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].Reason, Does.StartWith("Not introduced: Example.StubbedCaller.Uses() calls"));
        }

        /// <summary>
        /// What: a run with no entry at all leaves every stub in place, so it fails the artifact.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_RunWithoutEntries_FailsTheArtifact()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { UsesKey, GetterKey });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, null);

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(
                rows[0].Reason,
                Does.StartWith(
                    "Not introduced: Example.StubbedCaller.Uses(), Example.StubbedCaller.get_Value() call members"));
        }

        /// <summary>
        /// What: a stub whose parameter type is nested is covered by the entry the worker writes
        /// for it, because both spell the nested type in metadata form.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_StubWithANestedParameterTypeResolved_ReturnsNoRows()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { NestedParameterKey });
            HotReloadPreparedGroupFile resolved = CreateResolved(
                CreateEntry("Uses", new[] { NestedParameterMetadataName }));

            List<HotReloadIntroducedTypeOutcome> rows =
                HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, new[] { resolved });

            Assert.That(rows, Is.Empty);
        }

        /// <summary>
        /// What: the row of an unpatched stub whose parameter type is nested names that type in
        /// reflection form, the way the method rows of the same run name it.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_StubWithANestedParameterTypeUnresolved_NamesItInReflectionForm()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(new[] { NestedParameterKey });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, null);

            Assert.That(
                rows[0].Reason,
                Does.StartWith("Not introduced: Example.StubbedCaller.Uses(Example.Host+Inner) calls"));
        }

        /// <summary>
        /// What: an artifact that stubs nothing is never held back, even by a run with no entry.
        /// </summary>
        [Test]
        public void FindUncoveredTypes_ArtifactWithoutStubs_ReturnsNoRows()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact(Array.Empty<string>());

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadStubbedMemberCoverage.FindUncoveredTypes(artifact, null);

            Assert.That(rows, Is.Empty);
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifact(string[] callerStubKeys)
        {
            AssemblyName assemblyName = new AssemblyName("UloopIntroducedTypes_" + Guid.NewGuid().ToString("N"));
            Assembly assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            return new HotReloadIntroducedTypeArtifact(
                assembly,
                "stubbed-member-coverage-artifact.dll",
                "stubbed-member-coverage-artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        AssemblyName,
                        "original-mvid",
                        CallerMetadataName,
                        OwnerPath,
                        "caller-fingerprint",
                        "public class StubbedCaller { }",
                        callerStubKeys),
                    new HotReloadIntroducedTypeDescriptor(
                        AssemblyName,
                        "original-mvid",
                        SiblingMetadataName,
                        SiblingOwnerPath,
                        "sibling-fingerprint",
                        "public class StubbedSibling { }")
                });
        }

        private static HotReloadPreparedGroupFile CreateResolved(params TransformWorkerEntryDto[] entries)
        {
            return HotReloadPreparedGroupFile.Resolved(
                CreateFile(OwnerPath),
                entries,
                HotReloadEntryResolution.Result.Succeeded(new List<HotReloadEntryResolution.ResolvedEntry>()));
        }

        private static TransformWorkerEntryDto CreateEntry(string methodName, string[] parameterTypeFullNames = null)
        {
            return new TransformWorkerEntryDto
            {
                typeMetadataName = CallerMetadataName,
                methodName = methodName,
                parameterTypeFullNames = parameterTypeFullNames ?? Array.Empty<string>(),
                genericArity = 0
            };
        }

        private static HotReloadGroupFile CreateFile(string projectRelativePath)
        {
            return new HotReloadGroupFile(
                projectRelativePath,
                projectRelativePath,
                projectRelativePath,
                AssemblyName,
                FindCompilationAssembly(),
                HotReloadTypeHome.ScriptAssembliesUnderProject(ProjectRoot, AssemblyName),
                ProjectRoot,
                new HotReloadFileSinks(new List<string>(), null));
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static UnityCompilationAssembly FindCompilationAssembly()
        {
            foreach (UnityCompilationAssembly assembly
                in UnityEditor.Compilation.CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == AssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("The test assembly must be a compilation assembly.");
            return null;
        }
    }
}
