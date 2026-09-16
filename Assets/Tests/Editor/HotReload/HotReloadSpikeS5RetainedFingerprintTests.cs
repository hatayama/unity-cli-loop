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
                    + ". Only ordinary method bodies of an introduced type can be hot reloaded."));
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
