using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how a failed introduced-type compilation turns into the response rows a run reports.
    /// </summary>
    public class HotReloadIntroducedTypeCompileFailureOutcomesTests
    {
        private const string Prefix = "Introduced-type compilation failed: ";

        /// <summary>
        /// Verifies that a failure carrying no diagnostic still reports one row that names no
        /// owner, so a failure the compiler could not attribute is never dropped.
        /// </summary>
        [Test]
        public void Build_NoDiagnostics_ReturnsOneUnattributedRow()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation did not produce both DLL and PDB files.");

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly");

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Kind, Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed));
            Assert.That(rows[0].MetadataName, Is.EqualTo(string.Empty));
            Assert.That(rows[0].OriginalAssemblyName, Is.EqualTo("TargetAssembly"));
            Assert.That(rows[0].OwnerProjectRelativePath, Is.EqualTo(string.Empty));
            Assert.That(
                rows[0].Reason,
                Is.EqualTo(Prefix + "Introduced-type compilation did not produce both DLL and PDB files."));
        }

        /// <summary>
        /// Verifies that diagnostics are grouped into one row per owning file, in the order of the
        /// descriptors, with every message of that file joined into the row's reason.
        /// </summary>
        [Test]
        public void Build_DiagnosticsWithOwners_ReturnsOneRowPerOwnerWithItsMessages()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/Second.cs",
                        "CS0117: 'Shapes' does not contain a definition for 'TopRow'",
                        3,
                        5),
                    new HotReloadIntroducedTypeCompilerDiagnostic("Assets/Second.cs", "CS1002: ; expected", 9, 1),
                    new HotReloadIntroducedTypeCompilerDiagnostic("Assets/First.cs", "CS0246: missing type", 1, 1)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[]
                {
                    CreateDescriptor("Example.First", "Assets/First.cs"),
                    CreateDescriptor("Example.Second", "Assets/Second.cs")
                },
                "TargetAssembly");

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].MetadataName, Is.EqualTo("Example.First"));
            Assert.That(rows[0].OwnerProjectRelativePath, Is.EqualTo("Assets/First.cs"));
            Assert.That(rows[0].Reason, Is.EqualTo(Prefix + "Assets/First.cs(1,1): CS0246: missing type"));
            Assert.That(rows[1].MetadataName, Is.EqualTo("Example.Second"));
            Assert.That(rows[1].OwnerProjectRelativePath, Is.EqualTo("Assets/Second.cs"));
            Assert.That(
                rows[1].Reason,
                Is.EqualTo(
                    Prefix
                    + "Assets/Second.cs(3,5): CS0117: 'Shapes' does not contain a definition for 'TopRow'; "
                    + "Assets/Second.cs(9,1): CS1002: ; expected"));
        }

        /// <summary>
        /// Verifies that a diagnostic the compiler could not attribute to a source file is still
        /// reported, as a trailing row that names no owner.
        /// </summary>
        [Test]
        public void Build_DiagnosticWithoutOwner_AppendsOneUnattributedRow()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(string.Empty, "CS9999: something", 0, 0),
                    new HotReloadIntroducedTypeCompilerDiagnostic("Assets/First.cs", "CS0246: missing type", 1, 1)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly");

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].MetadataName, Is.EqualTo("Example.First"));
            Assert.That(rows[1].MetadataName, Is.EqualTo(string.Empty));
            Assert.That(rows[1].OwnerProjectRelativePath, Is.EqualTo(string.Empty));
            Assert.That(rows[1].Reason, Is.EqualTo(Prefix + "CS9999: something"));
        }

        /// <summary>
        /// Verifies that two types declared in one file share the single row of that file, named
        /// after the first descriptor, because a diagnostic identifies a file and not a type.
        /// </summary>
        [Test]
        public void Build_TwoTypesInOneOwner_UsesTheFirstDescriptor()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic("Assets/First.cs", "CS0246: missing type", 1, 1)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[]
                {
                    CreateDescriptor("Example.First", "Assets/First.cs"),
                    CreateDescriptor("Example.Companion", "Assets/First.cs")
                },
                "TargetAssembly");

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].MetadataName, Is.EqualTo("Example.First"));
            Assert.That(rows[0].OwnerProjectRelativePath, Is.EqualTo("Assets/First.cs"));
        }

        /// <summary>
        /// Verifies that a diagnostic without a source position reports its message alone, so a
        /// meaningless "(0,0)" never reaches the response.
        /// </summary>
        [Test]
        public void Build_DiagnosticWithoutPosition_OmitsThePosition()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic("Assets/First.cs", "CS0246: missing type", 0, 0)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly");

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Is.EqualTo(Prefix + "CS0246: missing type"));
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(
            string metadataName,
            string ownerProjectRelativePath)
        {
            return new HotReloadIntroducedTypeDescriptor(
                "OriginalAssembly",
                "original-mvid",
                metadataName,
                ownerProjectRelativePath,
                "fingerprint",
                "namespace Example { public class Placeholder { } }");
        }
    }
}
