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
    /// matter what the source says. The last test pins what the worker does with a body-only
    /// edit today, which is the behavior a later change has to move.
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

        /// <summary>What: planning a body-only edit against the recorded declaration still asks
        /// for a compile today, which is the behavior a later change starts from.</summary>
        [Test]
        public async Task Plan_BodyOnlyEditAgainstRecordedDeclaration_StillRequiresACompile()
        {
            HotReloadRetainedArtifactFixture fixture =
                await HotReloadRetainedArtifactFixture.CreateAsync("SpikeS5Pin", OriginalSource);
            string recordedFingerprint = fixture.RetainedFingerprint;
            File.WriteAllText(fixture.SourcePath, BodyEditedSource);

            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                    fixture.BuildPrepareInputWithArtifacts(
                        new[] { fixture.CreateRecordedArtifact(recordedFingerprint) }),
                    CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.files[0].introducedTypeDiagnostics,
                Has.Member(
                    HotReloadConstants.ChangedIntroducedTypeDiagnosticPrefix + RetainedTypeMetadataName));
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
