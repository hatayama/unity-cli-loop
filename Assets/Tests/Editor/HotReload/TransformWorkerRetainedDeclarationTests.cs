using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that a declaration a retained artifact already serves is taken out of the tree the
    /// transform binds against, only when the artifact record still describes the edited source,
    /// and that removing it does not move the source lines the shim reports.
    /// </summary>
    public sealed class TransformWorkerRetainedDeclarationTests
    {
        private const string RetainedTypeMetadataName = "Example.Retained";

        private const string EditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // The members the extended fixture's artifact type holds, so the edited source can change
        // one body and leave the others to the bodies the artifact assembly already runs. The
        // private method is what a source method the reload must not report as added looks like
        // once the artifact serves the type it belongs to.
        private const string ArtifactBackedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "\n"
            + "        public int Thrice()\n"
            + "        {\n"
            + "            return Value * 3;\n"
            + "        }\n"
            + "\n"
            + "        private int Hidden()\n"
            + "        {\n"
            + "            return Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // The same source with one ordinary method body changed, which is the only kind of edit a
        // type the domain already serves from an artifact can have applied to it.
        private const string ArtifactBackedBodyEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Twice()\n"
            + "        {\n"
            + "            return Value * 4;\n"
            + "        }\n"
            + "\n"
            + "        public int Thrice()\n"
            + "        {\n"
            + "            return Value * 3;\n"
            + "        }\n"
            + "\n"
            + "        private int Hidden()\n"
            + "        {\n"
            + "            return Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // The private method both edited sources hold, which is where a test splices an added
        // member so the insertion lands at the end of the retained declaration.
        private const string HiddenMember =
            "        private int Hidden()\n"
            + "        {\n"
            + "            return Value + 1;\n"
            + "        }\n";

        // The compiled caller's own method, which is where a test splices a member the caller
        // gains.
        private const string CallerReadMember =
            "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n";

        private const string AddedMethodMember =
            "        public int Extra()\n"
            + "        {\n"
            + "            return Value + 5;\n"
            + "        }\n";

        // An added method that reads a private method of the same type, which only the assembly
        // serving the type can answer.
        private const string AddedMethodCallingHiddenMember =
            "        public int Extra()\n"
            + "        {\n"
            + "            return Hidden() + 5;\n"
            + "        }\n";

        // An added field plus the method that reads it, because a field is only reported once an
        // emitted body names it.
        private const string AddedFieldAndReaderMembers =
            "        private int extra = 7;\n"
            + "\n"
            + "        public int Extra()\n"
            + "        {\n"
            + "            return extra;\n"
            + "        }\n";

        private const string AddedAutoPropertyMember =
            "        public int Extra { get; set; }\n";

        private const string AddedBodiedPropertyMember =
            "        public int Extra => Value + 5;\n";

        // A retained type whose only method is the one the default artifact holds, so a planning
        // test can compare a body edit of it against the same declaration left alone.
        private const string DependedOnSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Compute()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string DependedOnBodyEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public int Compute()\n"
            + "        {\n"
            + "            return Value * 3;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // A type this run really introduces, written in another file and depending on the retained
        // one, so its recorded fingerprint has to be stable across the retained type's body edits.
        private const string DependentSource =
            "namespace Example { public class Dependent { public int Read() { return new Retained().Compute(); } } }";

        // The region opens above the retained declaration and closes inside it, so blanking the
        // declaration takes the closing directive with it and leaves the text unparseable.
        private const string UnbalancedRegionSource =
            "namespace Example\n"
            + "{\n"
            + "#if UNITY_EDITOR\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "#endif\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "    }\n"
            + "\n"
            + "    public class Caller\n"
            + "    {\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return Retained.Value + 1;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        /// <summary>
        /// What: the shim reports the line the edited file really has for a method that follows a
        /// removed declaration, so taking the declaration out does not shift the mapping.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_KeepsSourceLineNumbers()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("KeepsSourceLineNumbers", EditedSource);

            TransformWorkerClientResult withoutRecord = await RunAsync(
                fixture,
                Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(FindReadEntry(withRecord).sourceStartLine, Is.EqualTo(FindReadEntry(withoutRecord).sourceStartLine));
            Assert.That(withRecord.Output.shimSource, Does.Contain("Retained.Value + 1"));
        }

        /// <summary>
        /// What: the retained type stops being transformed as a source declaration once its record
        /// matches, so the run no longer reports rows for its members.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationRemoved_ReportsNoRowsForRetainedType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("ReportsNoRows", EditedSource);

            TransformWorkerClientResult withoutRecord = await RunAsync(
                fixture,
                Array.Empty<TransformWorkerIntroducedTypeArtifactDto>());
            TransformWorkerClientResult withRecord = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(withoutRecord.Success, Is.True, withoutRecord.ErrorMessage);
            Assert.That(withRecord.Success, Is.True, withRecord.ErrorMessage);
            Assert.That(CountRowsMentioning(withoutRecord, "Twice"), Is.GreaterThan(0));
            Assert.That(CountRowsMentioning(withRecord, "Twice"), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a record whose fingerprint no longer matches the edited source leaves the
        /// declaration in place, because the source is then newer than the artifact.
        /// </summary>
        [Test]
        public async Task Transform_RecordFingerprintDoesNotMatch_KeepsDeclarationInBinding()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("FingerprintMismatch", EditedSource);

            TransformWorkerClientResult tampered = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(new string('0', 64)) });

            Assert.That(tampered.Success, Is.True, tampered.ErrorMessage);
            Assert.That(CountRowsMentioning(tampered, "Twice"), Is.GreaterThan(0));
        }

        /// <summary>
        /// What: an artifact record the worker cannot trust fails the whole run instead of
        /// producing a shim, because the caller advances to revert and compile on success.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactIdentityMismatch_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("IdentityMismatch", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            mismatched.assemblyFullName =
                HotReloadRetainedArtifactFixture.ReadAssemblyFullName(fixture.TargetAssemblyPath);

            TransformWorkerClientResult result = await RunAsync(fixture, new[] { mismatched });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: an untrusted artifact takes the whole group down, so a run of two edited files
        /// produces no entries and no shim for either of them instead of reporting the artifact as
        /// one file's diagnostic while the other file is still transformed and applied.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactIdentityMismatchInGroup_FailsEveryFile()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("GroupIdentityMismatch", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto mismatched =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            mismatched.assemblyFullName =
                HotReloadRetainedArtifactFixture.ReadAssemblyFullName(fixture.TargetAssemblyPath);

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildGroupTransformInput(new[] { mismatched }),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
            Assert.That(result.Output, Is.Null.Or.Property("shimSource").Null.Or.Property("shimSource").Empty);
        }

        /// <summary>
        /// What: the worker itself refuses a record whose type names no owning file, so a request
        /// that did not pass through the client's own check still fails the run instead of
        /// silently leaving the declaration in the tree and binding the type from source.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactTypeWithoutOwner_FailsRunInsideTheWorker()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("WorkerWithoutOwner", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto ownerless =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            ownerless.types[0].ownerProjectRelativePath = string.Empty;

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunWorkerAsync(
                fixture.BuildTransformInput(new[] { ownerless }),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: a record that lists no type fails the run inside the worker, because its
        /// reference would enter the compilation while no declaration is taken out of the binding
        /// tree and the type would bind from source again.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactWithoutTypes_FailsRunInsideTheWorker()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("WorkerWithoutTypes", EditedSource);
            TransformWorkerIntroducedTypeArtifactDto typeless =
                fixture.CreateRecordedArtifact(fixture.RetainedFingerprint);
            typeless.types = Array.Empty<TransformWorkerIntroducedTypeArtifactTypeDto>();

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunWorkerAsync(
                fixture.BuildTransformInput(new[] { typeless }),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: a transform that carries records but does not name the assembly generation they
        /// were planned against fails the run inside the worker, because the recorded identities
        /// would be rebuilt from an empty identity and no declaration would match its record.
        /// </summary>
        [Test]
        public async Task Transform_ArtifactsWithoutTargetIdentity_FailsRunInsideTheWorker()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("WorkerWithoutTargetIdentity", EditedSource);
            TransformWorkerInputDto input =
                fixture.BuildTransformInput(new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });
            input.targetAssemblyName = string.Empty;
            input.targetAssemblyMvid = string.Empty;

            TransformWorkerClientResult result = await HotReloadCompositionRoot.Services.TransformWorkerClient.RunWorkerAsync(
                input,
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: a preprocessor region that closes inside the retained declaration makes the
        /// blanked text unparseable, and the run fails instead of transforming the file against a
        /// tree the parser had to guess at.
        /// </summary>
        [Test]
        public async Task Transform_BlankedDeclarationLeavesUnbalancedRegion_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("UnbalancedRegion", UnbalancedRegionSource);

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
        }

        /// <summary>
        /// What: editing one ordinary method body of a type a retained artifact serves patches that
        /// method on the artifact assembly and leaves every other member of the type to the bodies
        /// the artifact already runs, instead of refusing the type as not compiled yet.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationMethodBodyEdited_PatchesOnlyTheEditedMethod()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "BodyEditedMethod",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, ArtifactBackedBodyEditedSource);

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] patched = FindEntries(result, "Twice");
            Assert.That(patched.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(patched[0].typeMetadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(patched[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindEntries(result, "Thrice"), Is.Empty, DescribeRows(result));
            TransformWorkerUnchangedMethodDto[] unchanged = FindUnchanged(result, "Thrice");
            Assert.That(unchanged.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(unchanged[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindSkippedCodes(result, "Hidden"), Is.Empty, DescribeRows(result));
            Assert.That(FindEntries(result, "Hidden"), Is.Empty, DescribeRows(result));
            Assert.That(
                FindSkippedCodes(result, "Twice"),
                Has.None.EqualTo(HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced),
                DescribeRows(result));
            Assert.That(result.Output.files[0].addedFieldNames, Is.Empty);
        }

        /// <summary>
        /// What: a body edit of a type a retained artifact serves reports no skip row for the
        /// type's constructor, because the assembly the domain loaded already runs it and this
        /// edit never touched it. Without the suppression every reload of such a file told the
        /// reader to run 'uloop compile' over a member they had not changed.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationHasAConstructor_ReportsNoSkipRowForIt()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "ConstructorNotReported",
                    WithConstructor(ArtifactBackedSource),
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, WithConstructor(ArtifactBackedBodyEditedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            // Why the patched row is asserted too: a run that reported nothing at all for the type
            // would have no constructor row either, and that is a different failure.
            Assert.That(FindEntries(result, "Twice").Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(FindSkippedCodes(result, ".ctor"), Is.Empty, DescribeRows(result));
        }

        /// <summary>
        /// What: a source whose bodies match the artifact again leaves nothing to patch, because
        /// the declaration is taken out of the binding tree the way an unedited retained type is.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationBodyMatchesTheRecord_ReportsNoRowsForTheType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "BodyMatchesRecord",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(fixture.RetainedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntriesOfRetainedType(result), Is.Empty, DescribeRows(result));
            Assert.That(FindUnchangedOfRetainedType(result), Is.Empty, DescribeRows(result));
            // Why the skips are asserted as well: a declaration left in the tree is reported as a
            // type that is not compiled yet, which produces skip rows alone - the two assertions
            // above would still hold.
            Assert.That(FindSkippedCodesOfRetainedType(result), Is.Empty, DescribeRows(result));
        }

        /// <summary>
        /// What: a body edit of a retained type whose declaration escapes its own name is patched
        /// too. The changed-body keys and the keys the emit stages spell a method with are both
        /// built from the declaration, so `@Retained` cannot make the two disagree and report an
        /// edited method as one whose body the artifact already runs.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationNameIsEscaped_PatchesTheEditedMethod()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "EscapedTypeName",
                    WithEscapedTypeName(ArtifactBackedSource),
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, WithEscapedTypeName(ArtifactBackedBodyEditedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] patched = FindEntries(result, "Twice");
            Assert.That(patched.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(patched[0].typeMetadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(patched[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
        }

        /// <summary>
        /// What: a getter-only property body edit of a retained type whose declaration escapes its
        /// own name is patched on the artifact assembly. The changed-body keys and the key the emit
        /// stage spells a property with are both built from the declaration, so `@Retained` cannot
        /// make the two disagree and leave an edited getter reported as one the artifact runs.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationNameIsEscaped_PatchesTheEditedPropertyGetter()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "EscapedTypeNameGetter",
                    WithEscapedTypeName(WithNumberProperty(ArtifactBackedSource)),
                    new[] { "Twice", "Thrice", "get_Number" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithEscapedTypeName(WithEditedNumberProperty(ArtifactBackedSource)));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] patched = FindEntries(result, "get_Number");
            Assert.That(patched.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(patched[0].typeMetadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(patched[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindUnchanged(result, "get_Number"), Is.Empty, DescribeRows(result));
        }

        /// <summary>
        /// What: a property getter of a retained type is reported as unchanged in the assembly
        /// serving the type, not patched as a getter of the edited file's own assembly. The
        /// comparison that kept the declaration reported that no accessor body changed, while the
        /// ordinary getter path decides that from a baseline snapshot this type has none of.
        /// </summary>
        [Test]
        public async Task Transform_RetainedDeclarationHasAPropertyGetter_ReportsTheGetterAsUnchanged()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "PropertyGetter",
                    WithNumberProperty(ArtifactBackedSource),
                    new[] { "Twice", "Thrice", "get_Number" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, WithNumberProperty(ArtifactBackedBodyEditedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntries(result, "get_Number"), Is.Empty, DescribeRows(result));
            TransformWorkerUnchangedMethodDto[] unchanged = FindUnchanged(result, "get_Number");
            Assert.That(unchanged.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(unchanged[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindEntries(result, "Twice").Length, Is.EqualTo(1), DescribeRows(result));
        }

        /// <summary>
        /// What: a type this run introduces records the same declaration fingerprint whether the
        /// retained type it depends on was body-edited or left alone, so a body edit of the
        /// dependency does not make the new type look like a different definition.
        /// </summary>
        [Test]
        public async Task Prepare_RetainedDependencyBodyEdited_KeepsTheDependentFingerprint()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithSiblingSourceAsync(
                    "DependentFingerprint",
                    DependedOnSource,
                    DependentSource);
            string recordedFingerprint = fixture.RetainedFingerprint;

            TransformWorkerFileOutputDto[] unedited = await PlanGroupAsync(fixture, recordedFingerprint);
            File.WriteAllText(fixture.SourcePath, DependedOnBodyEditedSource);
            TransformWorkerFileOutputDto[] bodyEdited = await PlanGroupAsync(fixture, recordedFingerprint);

            Assert.That(FindReuse(unedited[0]).bodyEdited, Is.False);
            Assert.That(FindReuse(bodyEdited[0]).bodyEdited, Is.True);
            Assert.That(
                FindDependentFingerprint(bodyEdited[1]),
                Is.EqualTo(FindDependentFingerprint(unedited[1])));
        }

        /// <summary>
        /// What: an ordinary method added to a type a retained artifact serves is emitted as an
        /// added method on the artifact assembly, instead of the type being refused as one no
        /// patch target holds.
        /// </summary>
        [Test]
        public async Task Transform_OrdinaryMethodAddedToRetainedType_EmitsAnAddedMethod()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedMethodOnRetained",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, WithAddedMember(ArtifactBackedSource, AddedMethodMember));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] added = FindEntries(result, "Extra");
            Assert.That(added.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(added[0].typeMetadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(
                added[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            Assert.That(added[0].homeAssemblyName, Is.EqualTo(fixture.ArtifactAssemblyName));
            Assert.That(FindUnchanged(result, "Twice").Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(FindUnchanged(result, "Thrice").Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(FindSkippedCodesOfRetainedType(result), Is.Empty, DescribeRows(result));
            Assert.That(result.Output.files[0].addedFieldNames, Is.Empty, DescribeRows(result));
        }

        /// <summary>
        /// What: adding a method and editing an existing body in the same reload applies both on
        /// the artifact: the edited method is patched and the new one is added.
        /// </summary>
        [Test]
        public async Task Transform_MethodAddedAndBodyEditedOnRetainedType_PatchesAndAddsInOneRun()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedMethodAndBodyEdit",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithAddedMember(ArtifactBackedBodyEditedSource, AddedMethodMember));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] patched = FindEntries(result, "Twice");
            Assert.That(patched.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                patched[0].patchKind,
                Is.Not.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            TransformWorkerEntryDto[] added = FindEntries(result, "Extra");
            Assert.That(added.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                added[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            Assert.That(FindUnchanged(result, "Thrice").Length, Is.EqualTo(1), DescribeRows(result));
        }

        /// <summary>
        /// What: a method added to a retained type can call a private method of that type, which
        /// the artifact holds, because the added method binds against the artifact the way it
        /// binds against a compiled type.
        /// </summary>
        [Test]
        public async Task Transform_AddedMethodCallsAPrivateMethodOfTheRetainedType_EmitsTheAddedMethod()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedMethodCallsPrivate",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithAddedMember(ArtifactBackedSource, AddedMethodCallingHiddenMember));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] added = FindEntries(result, "Extra");
            Assert.That(added.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                added[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            Assert.That(FindSkippedCodes(result, "Extra"), Is.Empty, DescribeRows(result));
        }

        /// <summary>
        /// What: a field added to a retained type is held by the added-field store and named in
        /// the run's added fields, because an emitted body reads it.
        /// </summary>
        [Test]
        public async Task Transform_FieldAddedToRetainedTypeAndRead_ReportsTheAddedField()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedFieldOnRetained",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithAddedMember(ArtifactBackedSource, AddedFieldAndReaderMembers));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.files[0].addedFieldNames,
                Does.Contain(RetainedTypeMetadataName + ".extra"),
                DescribeRows(result));
            TransformWorkerEntryDto[] added = FindEntries(result, "Extra");
            Assert.That(added.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                added[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
        }

        /// <summary>
        /// What: a field added to a compiled type whose initializer reads a public static member
        /// of a type the artifact serves is emittable, because that member is already live. The
        /// retained type is body-edited in the same run so its declaration stays in the tree,
        /// which is what makes the initializer read a source symbol. A public static member an
        /// artifact already holds is not a same-file addition, so the read is emittable.
        /// </summary>
        [Test]
        public async Task Transform_AddedFieldInitializerReadsARetainedTypesStaticMember_ReportsTheAddedField()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedFieldReadsRetained",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithCallerReadingTheRetainedStatic(ArtifactBackedBodyEditedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.files[0].addedFieldNames,
                Does.Contain("Example.Caller.extra"),
                DescribeRows(result));
            TransformWorkerEntryDto[] added = FindEntries(result, "Extra");
            Assert.That(added.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(added[0].typeMetadataName, Is.EqualTo("Example.Caller"));
            Assert.That(
                added[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
        }

        /// <summary>
        /// What: an auto property added to a retained type is emitted as two added accessors, the
        /// way one added to a compiled type is.
        /// </summary>
        [Test]
        public async Task Transform_AutoPropertyAddedToRetainedType_EmitsBothAccessorsAsAddedMethods()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedAutoPropertyOnRetained",
                    ArtifactBackedSource,
                    new[] { "Twice", "Thrice" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithAddedMember(ArtifactBackedSource, AddedAutoPropertyMember));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] getter = FindEntries(result, "get_Extra");
            Assert.That(getter.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(getter[0].typeMetadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(
                getter[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            TransformWorkerEntryDto[] setter = FindEntries(result, "set_Extra");
            Assert.That(setter.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                setter[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
        }

        /// <summary>
        /// What: a property with a body added to a retained type is emitted as one added getter,
        /// while the property the artifact already serves stays an unchanged row.
        /// </summary>
        [Test]
        public async Task Transform_BodiedPropertyAddedToRetainedType_EmitsTheGetterAsAnAddedMethod()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateWithArtifactMethodsAsync(
                    "AddedBodiedPropertyOnRetained",
                    WithNumberProperty(ArtifactBackedSource),
                    new[] { "Twice", "Thrice", "get_Number" },
                    new[] { "Hidden" });
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                WithAddedMember(WithNumberProperty(ArtifactBackedSource), AddedBodiedPropertyMember));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                new[] { fixture.CreateRecordedArtifact(recordedFingerprint) });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto[] getter = FindEntries(result, "get_Extra");
            Assert.That(getter.Length, Is.EqualTo(1), DescribeRows(result));
            Assert.That(
                getter[0].patchKind,
                Is.EqualTo(HotReloadConstants.PatchKindAddedMethod),
                DescribeRows(result));
            Assert.That(FindEntries(result, "get_Number"), Is.Empty, DescribeRows(result));
            Assert.That(FindUnchanged(result, "get_Number").Length, Is.EqualTo(1), DescribeRows(result));
        }

        // The same declaration with one more member on the retained type, which is the edit the
        // added-member machinery has to apply on the artifact.
        private static string WithAddedMember(string source, string member)
        {
            return source.Replace(HiddenMember, HiddenMember + "\n" + member, StringComparison.Ordinal);
        }

        // The same declaration with a field added to the compiled caller whose initializer reads
        // a public static member of the type the artifact serves, plus the method that reads the
        // field so an emitted body names it.
        private static string WithCallerReadingTheRetainedStatic(string source)
        {
            return source.Replace(
                CallerReadMember,
                "        private int extra = Retained.Value + 1;\n"
                + "\n"
                + CallerReadMember
                + "\n"
                + "        public int Extra()\n"
                + "        {\n"
                + "            return extra;\n"
                + "        }\n",
                StringComparison.Ordinal);
        }

        // The same declaration with its name escaped, which is a legal spelling of it that the
        // symbol reports without the escape - the two ways of naming the type the emit stages have
        // to agree on.
        private static string WithEscapedTypeName(string source)
        {
            return source.Replace("public class Retained", "public class @Retained", StringComparison.Ordinal);
        }

        // The same declaration with an explicit constructor, which is a member kind hot reload
        // cannot patch and so reports - the row a body edit of a retained type must not produce.
        private static string WithConstructor(string source)
        {
            return source.Replace(
                "        public static int Value = 1;\n",
                "        public static int Value = 1;\n\n        public Retained()\n        {\n        }\n",
                StringComparison.Ordinal);
        }

        // The same declaration with a property whose getter has a body, which the artifact serves
        // as get_Number.
        private static string WithNumberProperty(string source)
        {
            return source.Replace(
                "        public static int Value = 1;\n",
                "        public static int Value = 1;\n\n        public int Number => Value;\n",
                StringComparison.Ordinal);
        }

        // The same declaration with only the getter body of that property edited, which is the one
        // difference a reload of a retained type has to read as a body it can patch.
        private static string WithEditedNumberProperty(string source)
        {
            return source.Replace(
                "        public static int Value = 1;\n",
                "        public static int Value = 1;\n\n        public int Number => Value + 1;\n",
                StringComparison.Ordinal);
        }

        private static async Task<TransformWorkerClientResult> RunAsync(
            HotReloadRetainedArtifactFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto[] artifacts)
        {
            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildTransformInput(artifacts),
                CancellationToken.None);
        }

        private static async Task<TransformWorkerFileOutputDto[]> PlanGroupAsync(
            HotReloadRetainedArtifactFixture fixture,
            string recordedFingerprint)
        {
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildPrepareGroupInputWithArtifacts(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Output.files;
        }

        private static TransformWorkerIntroducedTypeReuseDto FindReuse(TransformWorkerFileOutputDto file)
        {
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].metadataName, Is.EqualTo(RetainedTypeMetadataName));
            return file.introducedTypeReuses[0];
        }

        private static string FindDependentFingerprint(TransformWorkerFileOutputDto file)
        {
            foreach (TransformWorkerIntroducedTypeDto introducedType in file.introducedTypes)
            {
                if (introducedType.metadataName == "Example.Dependent")
                {
                    return introducedType.declarationFingerprint;
                }
            }

            Assert.Fail("Planning did not report the dependent type.");
            return null;
        }

        private static TransformWorkerEntryDto[] FindEntries(
            TransformWorkerClientResult result,
            string methodName)
        {
            List<TransformWorkerEntryDto> found = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    found.Add(entry);
                }
            }

            return found.ToArray();
        }

        private static TransformWorkerEntryDto[] FindEntriesOfRetainedType(TransformWorkerClientResult result)
        {
            List<TransformWorkerEntryDto> found = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == RetainedTypeMetadataName)
                {
                    found.Add(entry);
                }
            }

            return found.ToArray();
        }

        private static TransformWorkerUnchangedMethodDto[] FindUnchanged(
            TransformWorkerClientResult result,
            string methodName)
        {
            List<TransformWorkerUnchangedMethodDto> found = new List<TransformWorkerUnchangedMethodDto>();
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                if (unchanged.methodName == methodName)
                {
                    found.Add(unchanged);
                }
            }

            return found.ToArray();
        }

        private static TransformWorkerUnchangedMethodDto[] FindUnchangedOfRetainedType(
            TransformWorkerClientResult result)
        {
            List<TransformWorkerUnchangedMethodDto> found = new List<TransformWorkerUnchangedMethodDto>();
            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                if (unchanged.typeMetadataName == RetainedTypeMetadataName)
                {
                    found.Add(unchanged);
                }
            }

            return found.ToArray();
        }

        private static HotReloadWorkerReasonCode[] FindSkippedCodesOfRetainedType(
            TransformWorkerClientResult result)
        {
            List<HotReloadWorkerReasonCode> codes = new List<HotReloadWorkerReasonCode>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(RetainedTypeMetadataName, StringComparison.Ordinal))
                {
                    codes.Add(skipped.reason.code);
                }
            }

            return codes.ToArray();
        }

        private static HotReloadWorkerReasonCode[] FindSkippedCodes(
            TransformWorkerClientResult result,
            string memberName)
        {
            List<HotReloadWorkerReasonCode> codes = new List<HotReloadWorkerReasonCode>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(memberName))
                {
                    codes.Add(skipped.reason.code);
                }
            }

            return codes.ToArray();
        }

        // What every row-level assertion prints on failure: which methods were patched, left
        // unchanged and skipped, because a wrong classification is only readable as a whole.
        private static string DescribeRows(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                lines.Add("entry " + entry.typeMetadataName + "::" + entry.methodName
                    + " home=" + (entry.homeAssemblyName ?? "<null>"));
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in result.Output.unchangedMethods)
            {
                lines.Add("unchanged " + unchanged.typeMetadataName + "::" + unchanged.methodName
                    + " home=" + (unchanged.homeAssemblyName ?? "<null>"));
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add("skipped " + skipped.method + " reason=" + skipped.reason.code);
            }

            return string.Join("\n", lines);
        }

        private static TransformWorkerEntryDto FindReadEntry(TransformWorkerClientResult result)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == "Read")
                {
                    return entry;
                }
            }

            Assert.Fail("No shim entry was emitted for the caller method.");
            return null;
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
    }
}
