using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S5 for hot reload: checks the second premise of the plan that lets an introduced
    /// type's body be edited without a compile. The structured fingerprint must tell an edit
    /// that only changes a member's body from one that changes the type's declaration, while a
    /// retained declaration and the artifact metadata share the same worker compilation. All
    /// three fingerprints come from the same fixture generation: a fingerprint's header carries
    /// the target assembly Mvid, so a second fixture would compare as a declaration change no
    /// matter what the source says. The planning tests then pin what the worker decides for each
    /// kind of difference, which is what the Editor acts on.
    /// </summary>
    public sealed class HotReloadSpikeS5RetainedFingerprintTests
    {
        private const string RetainedTypeMetadataName = "Example.Retained";

        // Measured, not assumed: WorkerSyntaxIndex.BuildSyntaxMethodKey spells a member key as
        // "<type metadata name>::<method name>(<parameter type keys>)".
        private const string TwiceMemberKey = "Example.Retained::Twice()";

        private const string OriginalSource =
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

        private const string BodyEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 3;\n"
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

        // Measured the same way: BuildSyntaxConstructorKey spells a constructor key as
        // "<type metadata name>::.ctor(<parameter type keys>)".
        private const string ConstructorMemberKey = "Example.Retained::.ctor()";

        private const string ConstructorSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        private readonly int seed;\n"
            + "\n"
            + "        public Retained()\n"
            + "        {\n"
            + "            seed = 2;\n"
            + "        }\n"
            + "\n"
            + "        public int Seed()\n"
            + "        {\n"
            + "            return seed;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string ConstructorBodyEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        private readonly int seed;\n"
            + "\n"
            + "        public Retained()\n"
            + "        {\n"
            + "            seed = 3;\n"
            + "        }\n"
            + "\n"
            + "        public int Seed()\n"
            + "        {\n"
            + "            return seed;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        // A parameter type written with a comment inside it. The fingerprint strips trivia before
        // it names a member, so the key it records carries no comment; anything that names the
        // same method from the raw declaration has to strip it too.
        private const string CommentedParameterSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice(System./* note */Int32 extra)\n"
            + "        {\n"
            + "            return Value * 2 + extra;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string CommentedParameterBodyEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice(System./* note */Int32 extra)\n"
            + "        {\n"
            + "            return Value * 3 + extra;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string DeclarationEditedSource =
            "namespace Example\n"
            + "{\n"
            + "    public class Retained\n"
            + "    {\n"
            + "        public static int Value = 1;\n"
            + "\n"
            + "        public static int Twice(int extra)\n"
            + "        {\n"
            + "            return Value * 2 + extra;\n"
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

        // The members the member-addition tests build a retained declaration out of. Spelling
        // them one by one keeps each test to the members it actually differs by, which is what
        // the fingerprint compares.
        private const string TwiceMember =
            "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 2;\n"
            + "        }\n"
            + "\n";

        private const string TwiceBodyEditedMember =
            "        public static int Twice()\n"
            + "        {\n"
            + "            return Value * 3;\n"
            + "        }\n"
            + "\n";

        private const string NumberMember =
            "        public int Number\n"
            + "        {\n"
            + "            get { return Value + 1; }\n"
            + "        }\n"
            + "\n";

        private const string NumberBodyEditedMember =
            "        public int Number\n"
            + "        {\n"
            + "            get { return Value + 2; }\n"
            + "        }\n"
            + "\n";

        // A property both of whose accessors have a body. One body hash covers the whole property,
        // so an edit of either one reads as an edit of both - which is why such a property stays
        // outside what a reload can patch.
        private const string BothAccessorsMember =
            "        public static int Both\n"
            + "        {\n"
            + "            get { return Value + 1; }\n"
            + "            set { Value = value; }\n"
            + "        }\n"
            + "\n";

        private const string BothAccessorsBodyEditedMember =
            "        public static int Both\n"
            + "        {\n"
            + "            get { return Value + 2; }\n"
            + "            set { Value = value; }\n"
            + "        }\n"
            + "\n";

        private const string AddedMethodMember =
            "        public static int Extra()\n"
            + "        {\n"
            + "            return Value * 4;\n"
            + "        }\n"
            + "\n";

        private const string AddedFieldMember =
            "        private int extra;\n"
            + "\n";

        private const string AddedPropertyMember =
            "        public int Extra\n"
            + "        {\n"
            + "            get { return 1; }\n"
            + "        }\n"
            + "\n";

        private const string AddedConstructorMember =
            "        public Retained()\n"
            + "        {\n"
            + "        }\n"
            + "\n";

        // The retained type written as an enum, so a test can describe what adding a member at
        // the tail of an enum does to the fingerprint of a type that has no method members at all.
        private const string RetainedEnumSource =
            "namespace Example\n"
            + "{\n"
            + "    public enum Retained\n"
            + "    {\n"
            + "        First,\n"
            + "        Second\n"
            + "    }\n"
            + "}\n";

        private const string RetainedEnumTailAddedSource =
            "namespace Example\n"
            + "{\n"
            + "    public enum Retained\n"
            + "    {\n"
            + "        First,\n"
            + "        Second,\n"
            + "        Third\n"
            + "    }\n"
            + "}\n";

        /// <summary>What: editing only a method body of a retained introduced type leaves the
        /// fingerprint comparison at body-only and names exactly that member.</summary>
        [Test]
        public async Task Fingerprint_BodyOnlyEdit_ComparesAsBodyOnlyAndNamesTheMember()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Body", OriginalSource);
            HotReloadIntroducedTypeFingerprint original = ParseOrFail(fixture.RetainedFingerprint);
            File.WriteAllText(fixture.SourcePath, BodyEditedSource);
            HotReloadIntroducedTypeFingerprint edited = ParseOrFail(await PlanFingerprintAsync(fixture));

            HotReloadIntroducedTypeFingerprintComparison comparison =
                HotReloadIntroducedTypeFingerprint.Compare(original, edited);

            TestContext.Out.WriteLine("Changed body keys: " + string.Join(", ", comparison.ChangedBodyKeys));
            Assert.That(
                comparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.BodyOnly),
                string.Join("\n", comparison.Details));
            Assert.That(comparison.ChangedBodyKeys, Is.EqualTo(new[] { TwiceMemberKey }));
        }

        /// <summary>What: changing the declaration of a retained introduced type's member
        /// compares as a declaration change rather than a body edit.</summary>
        [Test]
        public async Task Fingerprint_DeclarationEdit_ComparesAsDeclarationChanged()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Decl", OriginalSource);
            HotReloadIntroducedTypeFingerprint original = ParseOrFail(fixture.RetainedFingerprint);
            File.WriteAllText(fixture.SourcePath, DeclarationEditedSource);
            HotReloadIntroducedTypeFingerprint edited = ParseOrFail(await PlanFingerprintAsync(fixture));

            HotReloadIntroducedTypeFingerprintComparison comparison =
                HotReloadIntroducedTypeFingerprint.Compare(original, edited);

            Assert.That(
                comparison.Kind,
                Is.EqualTo(HotReloadIntroducedTypeFingerprintDifference.DeclarationChanged),
                string.Join("\n", comparison.Details));
        }

        /// <summary>What: planning a body-only edit against the recorded declaration reuses the
        /// active type and reports that its bodies were edited, instead of asking for a
        /// compile.</summary>
        [Test]
        public async Task Plan_BodyOnlyEditAgainstRecordedDeclaration_ReusesTheActiveType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Reuse", OriginalSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BodyEditedSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].metadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.True);
        }

        /// <summary>What: a body-only edit of a method whose parameter type is written with a
        /// comment inside it still reuses the active type, because the keys the reload compares
        /// are normalized the same way the recorded fingerprint normalized them.</summary>
        [Test]
        public async Task Plan_BodyOnlyEditOfACommentedParameterMethod_ReusesTheActiveType()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Comment", CommentedParameterSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, CommentedParameterBodyEditedSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.True);
        }

        /// <summary>What: planning a declaration edit against the recorded declaration still asks
        /// for a compile and names the differences it found.</summary>
        [Test]
        public async Task Plan_DeclarationEditAgainstRecordedDeclaration_ReportsWhatChanged()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Decl2", OriginalSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, DeclarationEditedSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("Twice"));
            Assert.That(reason, Does.EndWith("."));
        }

        /// <summary>What: editing only a constructor body of a retained introduced type asks for a
        /// compile and names the member, because the artifact can only host ordinary method
        /// bodies.</summary>
        [Test]
        public async Task Plan_ConstructorBodyEditAgainstRecordedDeclaration_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Ctor", ConstructorSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, ConstructorBodyEditedSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            Assert.That(
                FindSingleDiagnostic(file),
                Is.EqualTo(
                    "Changed member body of introduced type requires a compile: "
                    + RetainedTypeMetadataName
                    + " Changed members: " + ConstructorMemberKey
                    + ". Only ordinary method bodies and getter-only property bodies of an introduced type can be hot reloaded."));
        }

        /// <summary>What: a recorded fingerprint this version cannot read asks for a compile and
        /// says the record is what it could not account for, rather than being read as a
        /// declaration that happens to differ everywhere.</summary>
        [Test]
        public async Task Plan_UnreadableRecord_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Unreadable", OriginalSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, "recorded-by-an-older-version");

            Assert.That(file.introducedTypeReuses, Is.Empty);
            Assert.That(
                FindSingleDiagnostic(file),
                Is.EqualTo(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: record."));
        }

        /// <summary>What: adding an ordinary method to a retained introduced type reuses the
        /// active type instead of asking for a compile, and reports no body edit.</summary>
        [Test]
        public async Task Plan_AddedOrdinaryMethod_ReusesTheActiveTypeWithoutABodyEdit()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddMethod",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberMember + AddedMethodMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].metadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.False);
        }

        /// <summary>What: adding an ordinary method while also editing an existing method body
        /// reuses the active type and reports the body edit, so both are applied in one
        /// reload.</summary>
        [Test]
        public async Task Plan_AddedOrdinaryMethodWithAnEditedBody_ReusesTheActiveTypeAndReportsTheBodyEdit()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddMethodAndBody",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceBodyEditedMember + NumberMember + AddedMethodMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.True);
        }

        /// <summary>What: adding a field to a retained introduced type reuses the active type,
        /// because the added-field store holds it rather than the artifact.</summary>
        [Test]
        public async Task Plan_AddedField_ReusesTheActiveType()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddField",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberMember + AddedFieldMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.False);
        }

        /// <summary>What: adding a property to a retained introduced type reuses the active
        /// type, because its accessors are added members like an added method.</summary>
        [Test]
        public async Task Plan_AddedProperty_ReusesTheActiveType()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddProperty",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberMember + AddedPropertyMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.False);
        }

        /// <summary>What: adding a constructor still asks for a compile, because only ordinary
        /// methods, fields and properties can be added to a retained type.</summary>
        [Test]
        public async Task Plan_AddedConstructor_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddCtor",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(AddedConstructorMember + TwiceMember + NumberMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("added:"));
        }

        /// <summary>What: editing only the body of a property whose getter is the sole accessor
        /// with a body reuses the active type and reports the body edit, the same way an ordinary
        /// method body edit does.</summary>
        [Test]
        public async Task Plan_GetterOnlyPropertyBodyEdit_ReusesTheActiveTypeAndReportsTheBodyEdit()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5GetterBody",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberBodyEditedMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].metadataName, Is.EqualTo(RetainedTypeMetadataName));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.True);
        }

        /// <summary>What: editing the body of a property that also has a setter body still asks for
        /// a compile and names the property, because one body hash covers both accessors and the
        /// setter body the reload cannot patch may be the one that changed.</summary>
        [Test]
        public async Task Plan_PropertyWithASetterBodyEdited_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5SetterBody",
                BuildRetainedSource(TwiceMember + BothAccessorsMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + BothAccessorsBodyEditedMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed member body of introduced type requires a compile: "
                    + RetainedTypeMetadataName));
            Assert.That(reason, Does.Contain("Both"));
        }

        /// <summary>What: adding a method while editing a getter-only property body reuses the
        /// active type and reports the body edit, so both are applied in one reload.</summary>
        [Test]
        public async Task Plan_AddedMethodWithAnEditedGetterBody_ReusesTheActiveTypeAndReportsTheBodyEdit()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddMethodAndProperty",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberBodyEditedMember + AddedMethodMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.True);
        }

        /// <summary>What: writing an edited getter body back to what the record holds reports no
        /// body edit, so the reload that follows takes the patch the previous one installed back
        /// out instead of leaving it in place.</summary>
        [Test]
        public async Task Plan_GetterBodyRestoredToTheRecordedDeclaration_ReportsNoBodyEdit()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5GetterRestored",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + NumberBodyEditedMember));
            File.WriteAllText(fixture.SourcePath, BuildRetainedSource(TwiceMember + NumberMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.False);
        }

        /// <summary>What: removing a member from a retained introduced type asks for a compile
        /// and names the removal, because the artifact still holds it.</summary>
        [Test]
        public async Task Plan_RemovedMethod_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5RemoveMethod",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BuildRetainedSource(NumberMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("removed:"));
        }

        /// <summary>What: reordering existing members without adding any still asks for a
        /// compile, because member order decides things the artifact already fixed.</summary>
        [Test]
        public async Task Plan_ReorderedMembers_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5Reorder",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BuildRetainedSource(NumberMember + TwiceMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("order"));
        }

        /// <summary>What: a method added between two existing members reuses the active type,
        /// because the order the recorded members keep among themselves is unchanged.</summary>
        [Test]
        public async Task Plan_MethodAddedBetweenExistingMembers_ReusesTheActiveType()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddBetween",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + AddedMethodMember + NumberMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(
                HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics),
                Is.Empty);
            Assert.That(file.introducedTypeReuses.Length, Is.EqualTo(1));
            Assert.That(file.introducedTypeReuses[0].bodyEdited, Is.False);
        }

        /// <summary>What: adding a method while also reordering the existing members asks for a
        /// compile, because the order difference is more than the insertion explains.</summary>
        [Test]
        public async Task Plan_AddedMethodWithReorderedMembers_RequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddAndReorder",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(NumberMember + TwiceMember + AddedMethodMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("order"));
        }

        /// <summary>What: removing one member while adding another asks for a compile and names
        /// the removal as the reason, listing the addition the reload could have applied under a
        /// wording that says it is not the cause, so the reader does not take the addition back
        /// out.</summary>
        [Test]
        public async Task Plan_RemovedMethodWithAnAddedMethod_RequiresACompileAndNamesOnlyTheRemoval()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5RemoveAndAdd",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(TwiceMember + AddedMethodMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("removed:"));
            Assert.That(reason, Does.Not.Contain("added:"));
            Assert.That(reason, Does.Contain("1 addition(s) not counted as differences because they apply on their own: Example.Retained::Extra()"));
        }

        /// <summary>What: adding a constructor ahead of the existing members asks for a compile and
        /// names the constructor only, leaving out the order difference the insertion itself
        /// explains.</summary>
        [Test]
        public async Task Plan_AddedConstructorBeforeExistingMembers_RequiresACompileAndOmitsTheExplainedOrder()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5AddCtorFirst",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(
                fixture.SourcePath,
                BuildRetainedSource(AddedConstructorMember + TwiceMember + NumberMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("added:"));
            Assert.That(reason, Does.Not.Contain("order"));
            Assert.That(reason, Does.Not.Contain("omitted"));
        }

        /// <summary>What: removing a member asks for a compile and names the removal only, leaving
        /// out the order difference the removal itself causes, which no edit short of putting the
        /// member back could clear.</summary>
        [Test]
        public async Task Plan_RemovedMethod_RequiresACompileAndOmitsTheOrderTheRemovalExplains()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5RemoveOnly",
                BuildRetainedSource(TwiceMember + NumberMember));
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BuildRetainedSource(TwiceMember));

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("removed:"));
            Assert.That(reason, Does.Not.Contain("order"), reason);
        }

        /// <summary>What: adding a member at the tail of an enum asks for a compile and names the
        /// added member only, leaving out the header and order differences that the added member
        /// itself accounts for.</summary>
        [Test]
        public async Task Plan_EnumTailAddition_RequiresACompileAndNamesOnlyTheAddedMember()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateAsync(
                "SpikeS5EnumTailAdd",
                RetainedEnumSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, RetainedEnumTailAddedSource);

            TransformWorkerFileOutputDto file = await PlanAgainstRecordAsync(fixture, recordedFingerprint);

            Assert.That(file.introducedTypeReuses, Is.Empty);
            string reason = FindSingleDiagnostic(file);
            Assert.That(
                reason,
                Does.StartWith(
                    "Changed introduced type requires a compile: " + RetainedTypeMetadataName
                    + " Declaration differences: "));
            Assert.That(reason, Does.Contain("added:enum:Third"));
            Assert.That(reason, Does.Not.Contain("header"), reason);
            Assert.That(reason, Does.Not.Contain("order"), reason);
            Assert.That(reason, Does.Not.Contain("omitted"), reason);
        }

        /// <summary>
        /// Writes a retained declaration out of the members a test cares about, so each test
        /// spells only what differs from the recorded declaration.
        /// </summary>
        private static string BuildRetainedSource(string members)
        {
            return "namespace Example\n"
                + "{\n"
                + "    public class Retained\n"
                + "    {\n"
                + "        public static int Value = 1;\n"
                + "\n"
                + members
                + "    }\n"
                + "}\n";
        }

        /// <summary>
        /// Plans the fixture's current source against one recorded declaration, which is the
        /// shape every planning test here needs.
        /// </summary>
        private static async Task<TransformWorkerFileOutputDto> PlanAgainstRecordAsync(
            HotReloadRetainedArtifactFixture fixture,
            string recordedFingerprint)
        {
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildPrepareInputWithArtifacts(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Output.files[0];
        }

        private static string FindSingleDiagnostic(TransformWorkerFileOutputDto file)
        {
            string[] rendered = HotReloadWorkerReasonTestText.RenderAll(file.introducedTypeDiagnostics);
            Assert.That(rendered.Length, Is.EqualTo(1), string.Join("\n", rendered));
            return rendered[0];
        }

        private static HotReloadIntroducedTypeFingerprint ParseOrFail(string text)
        {
            Assert.That(
                HotReloadIntroducedTypeFingerprint.TryParse(text, out HotReloadIntroducedTypeFingerprint value),
                Is.True,
                "The planned fingerprint is not in the structured format: " + text);
            return value;
        }

        /// <summary>
        /// Runs planning over the fixture's current source and returns the fingerprint it records
        /// for the retained type, so a test reads the value the worker really produced.
        /// </summary>
        private static async Task<string> PlanFingerprintAsync(HotReloadRetainedArtifactFixture fixture)
        {
            TransformWorkerClientResult planned =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildPrepareInput(),
                    CancellationToken.None);
            Assert.That(planned.Success, Is.True, planned.ErrorMessage);
            foreach (TransformWorkerIntroducedTypeDto introducedType in planned.Output.files[0].introducedTypes)
            {
                if (introducedType.metadataName == RetainedTypeMetadataName)
                {
                    return introducedType.declarationFingerprint;
                }
            }

            Assert.Fail("Planning did not report the retained type.");
            return null;
        }
    }
}
