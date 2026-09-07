using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using Mono.Cecil;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that a reference which moves from a source declaration to a retained artifact
    /// assembly normalizes back to the same original identity, so the declaration fingerprint
    /// describes the definition rather than where the definition currently lives.
    /// </summary>
    public class TransformWorkerIntroducedTypeBindingTests
    {
        private const string RetainedProjectRelativePath = "Assets/Retained.cs";

        private const string DependentProjectRelativePath = "Assets/Dependent.cs";

        // The records these tests use only have to be well formed; what they assert is the
        // fingerprint of a dependent type, never the removal of the retained declaration.
        private const string PlaceholderDeclarationFingerprint =
            "0000000000000000000000000000000000000000000000000000000000000000";

        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        private const string DirectDependentSource =
            "namespace Example { public class Dependent { public int Read(Retained retained) { return retained.Value; } } }";

        // Reaches the retained type without naming it: the only occurrence is inside the type
        // returned by the member the declaration calls. A fingerprint that collapses a bound
        // symbol to one type records the collection type and loses the retained type.
        // The compiled baseline of the edited file: the type the target assembly already holds,
        // and nothing else.
        private const string CompiledDependentSnapshotSource =
            "namespace Example { public class Dependent { public int Value() { return 1; } } }";

        // The same file after an edit that changes a method body and introduces an enum beside
        // the compiled type. An enum is a supported introduced type but not a class declaration,
        // and the two travel through different syntax nodes.
        private const string EditedSourceIntroducingAnEnum =
            "namespace Example { public class Dependent { public int Value() { return 2; } } "
            + "public enum IntroducedChoice { First } }";

        // The same file after an edit that changes a method body and introduces a type beside
        // the compiled one.
        private const string EditedSourceIntroducingAType =
            "namespace Example { public class Dependent { public int Value() { return 2; } } "
            + "public class Introduced { } }";

        private const string IndirectDependentSource =
            "using System.Collections.Generic; namespace Example { public static class Holder { public static List<Retained> All() { return null; } } public class Dependent { public int Count() { return Holder.All().Count; } } }";

        /// <summary>
        /// Verifies that the fingerprint of a type is unchanged when the type it depends on stops
        /// being a source declaration and is bound from a retained artifact assembly instead.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_DependencyMovedToArtifact_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("DependencyMovedToArtifact", DirectDependentSource);

            TransformWorkerClientResult beforeSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult afterSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(beforeSwitch.Success, Is.True, beforeSwitch.ErrorMessage);
            Assert.That(afterSwitch.Success, Is.True, afterSwitch.ErrorMessage);
            Assert.That(
                FindFingerprint(beforeSwitch, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(afterSwitch, "Example.Dependent")),
                "Moving the dependency into a retained artifact must not change the definition.");
        }

        /// <summary>
        /// Verifies that adding an artifact the declaration does not depend on leaves the
        /// fingerprint unchanged, so an unrelated retained type cannot invalidate it.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_UnrelatedArtifactAdded_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("UnrelatedArtifactAdded", DirectDependentSource);
            string unrelatedPath = Path.Combine(fixture.Directory, "Unrelated.dll");
            CreateArtifactAssembly(unrelatedPath, "UnrelatedArtifact", "Example", "Unrelated");
            TransformWorkerIntroducedTypeArtifactDto unrelatedArtifact = new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(unrelatedPath),
                referencePath = unrelatedPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Unrelated",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };

            TransformWorkerClientResult withoutArtifact = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult withArtifact = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, new[] { unrelatedArtifact },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(withoutArtifact.Success, Is.True, withoutArtifact.ErrorMessage);
            Assert.That(withArtifact.Success, Is.True, withArtifact.ErrorMessage);
            Assert.That(
                FindFingerprint(withArtifact, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(withoutArtifact, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that the retained type really binds against the artifact assembly and that the
        /// record is what maps its identity back: referencing the same artifact without a record
        /// leaves the artifact assembly identity in the fingerprint, so it stops matching the run
        /// in which the type was still a source declaration.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactReferencedWithoutRecord_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("ArtifactWithoutRecord", DirectDependentSource);

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult recorded = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult unrecorded = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { fixture.RetainedArtifactPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(recorded.Success, Is.True, recorded.ErrorMessage);
            Assert.That(unrecorded.Success, Is.True, unrecorded.ErrorMessage);
            Assert.That(
                FindFingerprint(recorded, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")));
            Assert.That(
                FindFingerprint(unrecorded, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")),
                "Without a record the artifact assembly identity must stay in the fingerprint.");
        }

        /// <summary>
        /// Verifies that the fingerprint changes when the artifact record attributes the retained
        /// type to a different original assembly, so normalization follows the recorded identity
        /// rather than erasing the dependency.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactOriginalIdentityChanged_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("ArtifactOriginalIdentityChanged", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto recorded = CreateRetainedArtifact(fixture);
            TransformWorkerIntroducedTypeArtifactDto reattributed = CreateRetainedArtifact(fixture);
            reattributed.types[0].originalAssemblyMvid = Guid.NewGuid().ToString();

            TransformWorkerClientResult first = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { recorded }, Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult second = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: false, new[] { reattributed }, Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(first.Success, Is.True, first.ErrorMessage);
            Assert.That(second.Success, Is.True, second.ErrorMessage);
            Assert.That(
                FindFingerprint(second, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(first, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that a type with the same metadata name coming from an assembly no artifact
        /// record names is not normalized, so the mapping cannot be driven by a metadata name alone.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_SameNameFromUnlistedAssembly_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("SameNameFromUnlistedAssembly", DirectDependentSource);
            string foreignPath = Path.Combine(fixture.Directory, "ForeignRetained.dll");
            CreateRetainedArtifactAssembly(foreignPath, "ForeignRetained");

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult unlisted = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { foreignPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(unlisted.Success, Is.True, unlisted.ErrorMessage);
            Assert.That(
                FindFingerprint(unlisted, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")),
                "An unlisted assembly must not be normalized just because the metadata name matches.");
        }

        /// <summary>
        /// Verifies that an artifact record claiming an identity the referenced assembly does not
        /// report produces a diagnostic instead of a descriptor.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactIdentityMismatch_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("ArtifactIdentityMismatch", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched = CreateRetainedArtifact(fixture);
            mismatched.assemblyFullName = ReadAssemblyFullName(fixture.TargetAssemblyPath);

            await AssertArtifactIsRejected(fixture, mismatched);
        }

        /// <summary>
        /// Verifies that an artifact record listing a type the referenced assembly does not hold
        /// produces a diagnostic instead of a descriptor.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_ArtifactMissingMetadataName_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("ArtifactMissingMetadataName", DirectDependentSource);
            TransformWorkerIntroducedTypeArtifactDto missing = CreateRetainedArtifact(fixture);
            missing.types[0].metadataName = "Example.Absent";

            await AssertArtifactIsRejected(fixture, missing);
        }

        /// <summary>
        /// Verifies that two artifacts normalizing to the same original type are rejected, because
        /// the fingerprint would otherwise depend on which record was consulted first.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_TwoArtifactsNormalizeToSameType_ReportsNoIntroducedType()
        {
            BindingFixture fixture = CreateFixture("TwoArtifactsNormalizeToSameType", DirectDependentSource);
            string secondPath = Path.Combine(fixture.Directory, "SecondRetained.dll");
            CreateRetainedArtifactAssembly(secondPath, "SecondRetained");
            TransformWorkerIntroducedTypeArtifactDto duplicate = new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(secondPath),
                referencePath = secondPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };

            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture), duplicate },
                    Array.Empty<string>()),
                CancellationToken.None);

            AssertPreparationWasRefused(result);
        }

        /// <summary>
        /// What: a type this reload introduced from an edited compiled file is not reported as an
        /// edit outside a method body. The reload did apply the declaration - it lives in the
        /// artifact assembly the run compiled - so telling the caller to run a compile for it
        /// would name work that is already done.
        /// </summary>
        [Test]
        public async Task Transform_WhenTheEditIntroducesAType_DoesNotWarnAboutEditsOutsideMethodBodies()
        {
            BindingFixture fixture = CreateFixture(
                "IntroducedTypeDrift",
                EditedSourceIntroducingAType,
                includeCompiledDependent: true);

            TransformWorkerClientResult result = await RunTransformAfterIntroducingAsync(
                fixture,
                CompiledDependentSnapshotSource,
                "Example.Introduced");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                CollectDriftWarnings(result),
                Is.Empty,
                "The reload applied the introduced type, so nothing about it requires a compile.");
        }

        /// <summary>
        /// What: an enum this reload introduced into an edited compiled file is not reported as an
        /// edit outside a method body either. An enum is a supported introduced type, and it
        /// reaches the drift check through a declaration node no type-declaration rewrite visits.
        /// </summary>
        [Test]
        public async Task Transform_WhenTheEditIntroducesAnEnum_DoesNotWarnAboutEditsOutsideMethodBodies()
        {
            BindingFixture fixture = CreateFixture(
                "IntroducedEnumDrift",
                EditedSourceIntroducingAnEnum,
                includeCompiledDependent: true);

            TransformWorkerClientResult result = await RunTransformAfterIntroducingAsync(
                fixture,
                CompiledDependentSnapshotSource,
                "Example.IntroducedChoice");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                CollectDriftWarnings(result),
                Is.Empty,
                "The reload applied the introduced enum, so nothing about it requires a compile.");
        }

        // The production two-step: one run plans the new type and reports the fingerprint the
        // compile stamps into the artifact record, and the next run transforms the same file with
        // that record in hand. Planning first is what makes the record acceptable.
        private static async Task<TransformWorkerClientResult> RunTransformAfterIntroducingAsync(
            BindingFixture fixture,
            string snapshotSource,
            string introducedMetadataName)
        {
            TransformWorkerClientResult planned = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            Assert.That(planned.Success, Is.True, planned.ErrorMessage);

            TransformWorkerInputDto input = CreateInput(
                fixture,
                includeRetainedSource: false,
                new[]
                {
                    CreateIntroducedArtifact(
                        fixture,
                        introducedMetadataName,
                        FindFingerprint(planned, introducedMetadataName))
                },
                Array.Empty<string>(),
                snapshotSource);
            // The declaration is only ever removed by a transform run; planning reports it.
            input.operation = null;
            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        // The record the reload writes after compiling the planned type into its own assembly:
        // same owner file, same metadata name, and the fingerprint the planning run reported.
        private static TransformWorkerIntroducedTypeArtifactDto CreateIntroducedArtifact(
            BindingFixture fixture,
            string metadataName,
            string declarationFingerprint)
        {
            int separator = metadataName.LastIndexOf('.');
            string artifactPath = Path.Combine(fixture.Directory, "IntroducedArtifact.dll");
            CreateArtifactAssembly(
                artifactPath,
                "IntroducedArtifact",
                metadataName.Substring(0, separator),
                metadataName.Substring(separator + 1));
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(artifactPath),
                referencePath = artifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = metadataName,
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = DependentProjectRelativePath,
                        declarationFingerprint = declarationFingerprint
                    }
                }
            };
        }

        private static List<string> CollectDriftWarnings(TransformWorkerClientResult result)
        {
            List<string> warnings = new List<string>();
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (string warning in file.declarationDriftWarnings ?? Array.Empty<string>())
                {
                    if (warning != null && warning.Contains("Edits outside method bodies"))
                    {
                        warnings.Add(warning);
                    }
                }
            }

            return warnings;
        }

        private static async Task AssertArtifactIsRejected(
            BindingFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto artifact)
        {
            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(
                CreateInput(fixture, includeRetainedSource: true, new[] { artifact }, Array.Empty<string>()),
                CancellationToken.None);

            AssertPreparationWasRefused(result);
        }

        private static void AssertPreparationWasRefused(TransformWorkerClientResult result)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                Assert.That(file.introducedTypes, Is.Empty);
                Assert.That(file.introducedTypeDiagnostics, Is.Not.Empty);
            }
        }

        /// <summary>
        /// Verifies that a declaration reaching the retained type only through a constructed
        /// generic and an array keeps its fingerprint when that type moves into an artifact.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_IndirectDependencyMovedToArtifact_KeepsFingerprint()
        {
            BindingFixture fixture = CreateFixture("IndirectDependencyMoved", IndirectDependentSource);

            TransformWorkerClientResult beforeSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult afterSwitch = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    new[] { CreateRetainedArtifact(fixture) },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(beforeSwitch.Success, Is.True, beforeSwitch.ErrorMessage);
            Assert.That(afterSwitch.Success, Is.True, afterSwitch.ErrorMessage);
            Assert.That(
                FindFingerprint(afterSwitch, "Example.Dependent"),
                Is.EqualTo(FindFingerprint(beforeSwitch, "Example.Dependent")));
        }

        /// <summary>
        /// Verifies that swapping the type behind a constructed generic and an array for a
        /// same-named type in another assembly changes the fingerprint, so a dependency that only
        /// appears inside a type argument or an element type is really recorded.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_IndirectDependencySwappedForForeignType_ChangesFingerprint()
        {
            BindingFixture fixture = CreateFixture("IndirectDependencySwapped", IndirectDependentSource);
            string foreignPath = Path.Combine(fixture.Directory, "ForeignRetained.dll");
            CreateRetainedArtifactAssembly(foreignPath, "ForeignRetained");

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            TransformWorkerClientResult foreignBound = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: false,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    new[] { foreignPath }),
                CancellationToken.None);

            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            Assert.That(foreignBound.Success, Is.True, foreignBound.ErrorMessage);
            Assert.That(
                FindFingerprint(foreignBound, "Example.Dependent"),
                Is.Not.EqualTo(FindFingerprint(sourceDeclared, "Example.Dependent")));
        }

        /// <summary>
        /// What: a declaration whose own record still matches is taken out of the binding tree
        /// even though it reads a type that is now served from a retained artifact, because the
        /// fingerprint is recomputed through the same normalization planning used. A tampered
        /// fingerprint on the same run keeps the declaration, so the check is not vacuous.
        /// </summary>
        [Test]
        public async Task Transform_DeclarationReadingRetainedType_StillMatchesItsRecord()
        {
            BindingFixture fixture = CreateFixture("DeclarationReadingRetainedType", DirectDependentSource);
            TransformWorkerClientResult planned = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            Assert.That(planned.Success, Is.True, planned.ErrorMessage);
            string dependentFingerprint = FindFingerprint(planned, "Example.Dependent");

            TransformWorkerClientResult matched = await RunTransformAsync(fixture, dependentFingerprint);
            TransformWorkerClientResult tampered = await RunTransformAsync(
                fixture,
                new string('0', 64));

            Assert.That(matched.Success, Is.True, matched.ErrorMessage);
            Assert.That(tampered.Success, Is.True, tampered.ErrorMessage);
            Assert.That(CountRowsMentioning(tampered, "Read"), Is.GreaterThan(0));
            Assert.That(CountRowsMentioning(matched, "Read"), Is.EqualTo(0));
        }

        private static async Task<TransformWorkerClientResult> RunTransformAsync(
            BindingFixture fixture,
            string dependentFingerprint)
        {
            TransformWorkerInputDto input = CreateInput(
                fixture,
                includeRetainedSource: false,
                new[]
                {
                    CreateRetainedArtifact(fixture),
                    CreateDependentArtifact(fixture, dependentFingerprint)
                },
                Array.Empty<string>());
            // The declaration is only ever removed by a transform run; planning reports it.
            input.operation = null;
            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static TransformWorkerIntroducedTypeArtifactDto CreateDependentArtifact(
            BindingFixture fixture,
            string declarationFingerprint)
        {
            string artifactPath = Path.Combine(fixture.Directory, "DependentArtifact.dll");
            CreateArtifactAssembly(artifactPath, "DependentArtifact", "Example", "Dependent");
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(artifactPath),
                referencePath = artifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Dependent",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = DependentProjectRelativePath,
                        declarationFingerprint = declarationFingerprint
                    }
                }
            };
        }

        private static int CountRowsMentioning(TransformWorkerClientResult result, string memberName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == memberName)
                {
                    count++;
                }
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(memberName))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Verifies that a declaration whose fingerprint matches a retained artifact is reported as
        /// a reuse of the active type instead of being introduced again, so a run can tell the
        /// reader which types it bound from what this domain already holds.
        /// </summary>
        [Test]
        public async Task PrepareIntroducedTypes_DeclarationMatchesActiveArtifact_ReportsReuse()
        {
            BindingFixture fixture = CreateFixture("DeclarationMatchesActive", DirectDependentSource);

            TransformWorkerClientResult sourceDeclared = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    Array.Empty<TransformWorkerIntroducedTypeArtifactDto>(),
                    Array.Empty<string>()),
                CancellationToken.None);
            Assert.That(sourceDeclared.Success, Is.True, sourceDeclared.ErrorMessage);
            string activeFingerprint = FindFingerprint(sourceDeclared, "Example.Retained");

            TransformWorkerClientResult reloaded = await TransformWorkerClient.RunAsync(
                CreateInput(
                    fixture,
                    includeRetainedSource: true,
                    new[] { CreateRetainedArtifactMatching(fixture, activeFingerprint) },
                    Array.Empty<string>()),
                CancellationToken.None);

            Assert.That(reloaded.Success, Is.True, reloaded.ErrorMessage);
            Assert.That(
                CollectMetadataNames(reloaded.Output),
                Does.Not.Contain("Example.Retained"),
                "A type this domain already holds must not be introduced a second time.");
            Assert.That(
                CollectReusedMetadataNames(reloaded.Output),
                Is.EqualTo(new[] { "Example.Retained" }));
        }

        private static List<string> CollectMetadataNames(TransformWorkerOutputDto output)
        {
            List<string> names = new List<string>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    names.Add(introducedType.metadataName);
                }
            }

            return names;
        }

        private static List<string> CollectReusedMetadataNames(TransformWorkerOutputDto output)
        {
            List<string> names = new List<string>();
            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                foreach (TransformWorkerIntroducedTypeReuseDto reuse in file.introducedTypeReuses)
                {
                    names.Add(reuse.metadataName);
                }
            }

            return names;
        }

        // Overloads are forbidden, so this distinct name builds the record with the fingerprint the
        // planner computed for the live declaration instead of the placeholder one.
        private static TransformWorkerIntroducedTypeArtifactDto CreateRetainedArtifactMatching(
            BindingFixture fixture,
            string declarationFingerprint)
        {
            TransformWorkerIntroducedTypeArtifactDto artifact = CreateRetainedArtifact(fixture);
            artifact.types[0].declarationFingerprint = declarationFingerprint;
            return artifact;
        }

        private static string FindFingerprint(TransformWorkerClientResult result, string metadataName)
        {
            foreach (TransformWorkerFileOutputDto file in result.Output.files)
            {
                foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
                {
                    if (introducedType.metadataName == metadataName)
                    {
                        return introducedType.declarationFingerprint;
                    }
                }
            }

            Assert.Fail("No introduced type named " + metadataName + " was reported.");
            return null;
        }

        private static BindingFixture CreateFixture(
            string name,
            string dependentSource,
            bool includeCompiledDependent = false)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "TestSources",
                "IntroducedTypeBinding",
                name);
            Directory.CreateDirectory(directory);
            string dependentSourcePath = Path.Combine(directory, "Dependent.cs");
            string retainedSourcePath = Path.Combine(directory, "Retained.cs");
            File.WriteAllText(dependentSourcePath, dependentSource);
            File.WriteAllText(
                retainedSourcePath,
                "namespace Example { public class Retained { public int Value; } }");
            string targetAssemblyPath = Path.Combine(directory, "BindingTarget.dll");
            string targetAssemblyMvid = includeCompiledDependent
                ? CreateTargetAssemblyWithCompiledDependent(targetAssemblyPath, "BindingTarget")
                : CreateArtifactAssembly(
                    targetAssemblyPath,
                    "BindingTarget",
                    "Example",
                    "Unrelated");
            string retainedArtifactPath = Path.Combine(directory, "RetainedArtifact.dll");
            CreateRetainedArtifactAssembly(retainedArtifactPath, "RetainedArtifact");
            return new BindingFixture(
                directory,
                dependentSourcePath,
                retainedSourcePath,
                targetAssemblyPath,
                "BindingTarget",
                targetAssemblyMvid,
                retainedArtifactPath);
        }

        private static TransformWorkerIntroducedTypeArtifactDto CreateRetainedArtifact(BindingFixture fixture)
        {
            return new TransformWorkerIntroducedTypeArtifactDto
            {
                assemblyFullName = ReadAssemblyFullName(fixture.RetainedArtifactPath),
                referencePath = fixture.RetainedArtifactPath,
                types = new[]
                {
                    new TransformWorkerIntroducedTypeArtifactTypeDto
                    {
                        metadataName = "Example.Retained",
                        originalAssemblyName = fixture.TargetAssemblyName,
                        originalAssemblyMvid = fixture.TargetAssemblyMvid,
                        ownerProjectRelativePath = RetainedProjectRelativePath,
                        declarationFingerprint = PlaceholderDeclarationFingerprint
                    }
                }
            };
        }

        private static TransformWorkerInputDto CreateInput(
            BindingFixture fixture,
            bool includeRetainedSource,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts,
            string[] extraReferencePaths,
            string dependentSnapshotSource = null)
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            List<TransformWorkerSourceDto> sources = new List<TransformWorkerSourceDto>
            {
                new TransformWorkerSourceDto
                {
                    sourcePath = fixture.DependentSourcePath,
                    projectRelativePath = "Assets/Dependent.cs",
                    // Without a snapshot the unit has no baseline, and the outside-method-body
                    // drift check the introduced type has to survive never runs at all.
                    snapshotSource = dependentSnapshotSource
                }
            };
            if (includeRetainedSource)
            {
                sources.Add(
                    new TransformWorkerSourceDto
                    {
                        sourcePath = fixture.RetainedSourcePath,
                        projectRelativePath = "Assets/Retained.cs"
                    });
            }

            List<string> referencePaths = new List<string>();
            foreach (string reference in compilationAssembly.allReferences)
            {
                if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                {
                    referencePaths.Add(Path.GetFullPath(reference));
                }
            }

            referencePaths.Add(Path.GetFullPath(fixture.TargetAssemblyPath));
            foreach (string extraReferencePath in extraReferencePaths)
            {
                referencePaths.Add(Path.GetFullPath(extraReferencePath));
            }

            return new TransformWorkerInputDto
            {
                operation = "prepareIntroducedTypes",
                sources = sources.ToArray(),
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = referencePaths.ToArray(),
                targetTypesAssemblyPath = fixture.TargetAssemblyPath,
                targetAssemblyName = fixture.TargetAssemblyName,
                targetAssemblyMvid = fixture.TargetAssemblyMvid,
                assemblySourcePaths = Array.Empty<string>(),
                changedSiblingSourcePaths = Array.Empty<string>(),
                introducedTypeArtifacts = artifacts
            };
        }

        private static string CreateArtifactAssembly(
            string path,
            string assemblyName,
            string typeNamespace,
            string typeName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition type = new TypeDefinition(
                    typeNamespace,
                    typeName,
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                assembly.MainModule.Types.Add(type);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

        // A target assembly that already holds the edited file's type, so the file reads as
        // compiled and the only new type is the one the edit introduces.
        private static string CreateTargetAssemblyWithCompiledDependent(string path, string assemblyName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition dependentType = new TypeDefinition(
                    "Example",
                    "Dependent",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                MethodDefinition valueMethod = new MethodDefinition(
                    "Value",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                    assembly.MainModule.TypeSystem.Int32);
                Mono.Cecil.Cil.ILProcessor il = valueMethod.Body.GetILProcessor();
                il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
                il.Append(il.Create(Mono.Cecil.Cil.OpCodes.Ret));
                dependentType.Methods.Add(valueMethod);
                assembly.MainModule.Types.Add(dependentType);
                assembly.Write(path);
            }

            using (ModuleDefinition module = ModuleDefinition.ReadModule(path))
            {
                return module.Mvid.ToString();
            }
        }

        private static void CreateRetainedArtifactAssembly(string path, string assemblyName)
        {
            AssemblyNameDefinition assemblyNameDefinition = new AssemblyNameDefinition(
                assemblyName,
                new Version(1, 0, 0, 0));
            using (AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
                assemblyNameDefinition,
                assemblyName,
                ModuleKind.Dll))
            {
                TypeDefinition retainedType = new TypeDefinition(
                    "Example",
                    "Retained",
                    CecilTypeAttributes.Public | CecilTypeAttributes.Class,
                    assembly.MainModule.TypeSystem.Object);
                FieldDefinition valueField = new FieldDefinition(
                    "Value",
                    Mono.Cecil.FieldAttributes.Public,
                    assembly.MainModule.TypeSystem.Int32);
                retainedType.Fields.Add(valueField);
                assembly.MainModule.Types.Add(retainedType);
                assembly.Write(path);
            }
        }

        private static string ReadAssemblyFullName(string path)
        {
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
            {
                return assembly.Name.FullName;
            }
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }

        private sealed class BindingFixture
        {
            public BindingFixture(
                string directory,
                string dependentSourcePath,
                string retainedSourcePath,
                string targetAssemblyPath,
                string targetAssemblyName,
                string targetAssemblyMvid,
                string retainedArtifactPath)
            {
                Directory = directory;
                DependentSourcePath = dependentSourcePath;
                RetainedSourcePath = retainedSourcePath;
                TargetAssemblyPath = targetAssemblyPath;
                TargetAssemblyName = targetAssemblyName;
                TargetAssemblyMvid = targetAssemblyMvid;
                RetainedArtifactPath = retainedArtifactPath;
            }

            public string Directory { get; }

            public string DependentSourcePath { get; }

            public string RetainedSourcePath { get; }

            public string TargetAssemblyPath { get; }

            public string TargetAssemblyName { get; }

            public string TargetAssemblyMvid { get; }

            public string RetainedArtifactPath { get; }
        }
    }
}
