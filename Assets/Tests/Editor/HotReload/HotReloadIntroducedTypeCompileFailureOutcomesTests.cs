using System;
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
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

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
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

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
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

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
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

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
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Is.EqualTo(Prefix + "CS0246: missing type"));
        }

        /// <summary>
        /// Verifies that a declaration the compiler never blamed is still reported as not
        /// compiled, so a type of a failed batch never disappears from the response.
        /// </summary>
        [Test]
        public void Build_DescriptorWithoutDiagnostics_ReportsItAsNotCompiled()
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
                    CreateDescriptor("Example.Second", "Assets/Second.cs")
                },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(Array.Empty<string>(), Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[1].Kind, Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed));
            Assert.That(rows[1].MetadataName, Is.EqualTo("Example.Second"));
            Assert.That(rows[1].OwnerProjectRelativePath, Is.EqualTo("Assets/Second.cs"));
            Assert.That(
                rows[1].Reason,
                Is.EqualTo(
                    "Not compiled: another declaration in the same introduced-type batch failed to "
                    + "compile, so this type was not introduced. Fix the failed file and rerun."));
        }

        /// <summary>
        /// Verifies that a missing-member diagnostic naming a member hot reload added earlier
        /// explains that an introduced type cannot see it, instead of reading as a typo.
        /// </summary>
        [Test]
        public void Build_DiagnosticNamesAnActiveAddedMember_AppendsTheAddedMemberHint()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/First.cs",
                        "CS1061: 'Widget' does not contain a definition for 'Clear' and no accessible extension method",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Clear" }, Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(
                rows[0].Reason,
                Does.EndWith(
                    "One or more of the missing members share a name with a hot reload addition, "
                    + "from this reload or an earlier one. If the missing member is that addition, "
                    + "an introduced type cannot see it: it compiles against the compiled "
                    + "assemblies and earlier introduced types only, so reloading the addition "
                    + "first does not help. Run 'uloop compile' to make the added members "
                    + "compiled, then rerun."));
        }

        /// <summary>
        /// Verifies that a missing-member diagnostic naming no added member leaves the reason as
        /// the compiler wrote it, so an ordinary typo is not explained away.
        /// </summary>
        [Test]
        public void Build_DiagnosticNamesNoActiveAddedMember_LeavesTheReasonAlone()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/First.cs",
                        "CS1061: 'Widget' does not contain a definition for 'Clear' and no accessible extension method",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Other" }, Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Does.Not.Contain("hot reload addition"));
        }

        /// <summary>
        /// Verifies that a CS0117 naming an enum member this reload adds to a compiled enum points
        /// at the enum-member warning instead of the added-member hint, even when an added member
        /// shares the name: no reordering of the reload makes an added enum member visible.
        /// </summary>
        [Test]
        public void Build_DiagnosticNamesAnAddedEnumMember_AppendsTheEnumMemberHint()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/First.cs",
                        "CS0117: 'Shape' does not contain a definition for 'Third'",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Third" }, new[] { "Third" }));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Does.Contain("an enum member this reload adds to a compiled enum"));
            Assert.That(rows[0].Reason, Does.Not.Contain("hot reload addition"));
        }

        /// <summary>
        /// Verifies that an owner path holding an apostrophe does not swallow the quoted member
        /// name, so the added-member hint still reaches the reader.
        /// </summary>
        [Test]
        public void Build_OwnerPathHoldsAnApostrophe_StillAppendsTheAddedMemberHint()
        {
            const string ownerPath = "Assets/Player's/First.cs";
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        ownerPath,
                        "CS1061: 'Widget' does not contain a definition for 'Clear' and no accessible extension method",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", ownerPath) },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Clear" }, Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Does.EndWith("then rerun."));
        }

        /// <summary>
        /// Verifies that a missing static member diagnostic naming an added member is explained
        /// the same way as the instance-member one.
        /// </summary>
        [Test]
        public void Build_StaticMemberDiagnosticNamesAnActiveAddedMember_AppendsTheAddedMemberHint()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/First.cs",
                        "CS0117: 'Widget' does not contain a definition for 'Reset'",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Reset" }, Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Does.EndWith("then rerun."));
        }

        /// <summary>
        /// Verifies that a diagnostic of another error code is left alone even when its second
        /// quoted token happens to name an added member.
        /// </summary>
        [Test]
        public void Build_UnrelatedErrorCodeQuotesAnAddedMemberName_LeavesTheReasonAlone()
        {
            HotReloadIntroducedTypeCompilerResult compileResult = HotReloadIntroducedTypeCompilerResult.Failure(
                "Introduced-type compilation reported errors.",
                new[]
                {
                    new HotReloadIntroducedTypeCompilerDiagnostic(
                        "Assets/First.cs",
                        "CS0122: 'Widget' declares 'Clear' with a protection level that hides it",
                        4,
                        9)
                });

            List<HotReloadIntroducedTypeOutcome> rows = HotReloadIntroducedTypeCompileFailureOutcomes.Build(
                compileResult,
                new[] { CreateDescriptor("Example.First", "Assets/First.cs") },
                "TargetAssembly",
                new HotReloadIntroducedTypeAddedMemberNames(new[] { "Clear" }, Array.Empty<string>()));

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Reason, Does.Not.Contain("hot reload addition"));
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
