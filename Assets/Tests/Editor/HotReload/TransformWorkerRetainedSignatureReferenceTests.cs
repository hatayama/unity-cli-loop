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
    /// Verifies that a run refuses to change a retained type another retained type names in its
    /// signatures while that other type stays bound from the retained assembly, because the edit
    /// would leave two definitions of the changed type in one compilation.
    /// </summary>
    public sealed class TransformWorkerRetainedSignatureReferenceTests
    {
        private const string RetainedTypeMetadataName = "Example.Retained";

        // The referrer is not part of the edited source in most tests, so its record only has to
        // carry a fingerprint for the request to be well formed.
        private const string UnplannedReferrerFingerprint =
            "0000000000000000000000000000000000000000000000000000000000000000";

        private const string RetainedSource =
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

        private const string ComputeBody = "            return Value * 2;\n";

        private const string ComputeMember =
            "        public int Compute()\n"
            + "        {\n"
            + ComputeBody
            + "        }\n";

        private const string AddedMethodMember =
            "        public int Extra()\n"
            + "        {\n"
            + "            return Value + 5;\n"
            + "        }\n";

        // The referrer as source, matching the artifact's public-return shape, so a test can put
        // it into the same run as the type it names.
        private const string ReferrerDeclaration =
            "\n"
            + "    public class RetainedFactory\n"
            + "    {\n"
            + "        public Retained Make()\n"
            + "        {\n"
            + "            return new Retained();\n"
            + "        }\n"
            + "    }\n";

        private const string ReferrerMakeBody = "            return new Retained();\n";

        /// <summary>
        /// What: adding a method to a retained type that an unchanged retained type returns from a
        /// public method fails the run and names both types and 'uloop compile', instead of
        /// reporting the added method as unbound and dropping the members added earlier.
        /// </summary>
        [Test]
        public async Task Transform_MethodAddedToRetainedTypeReferencedByAnUnchangedRetainedTypesSignature_FailsRunAndNamesTheReferrer()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerAddedMethod",
                RetainedSource,
                RetainedArtifactReferrerShape.PublicReturnType);
            File.WriteAllText(fixture.SourcePath, WithAddedMethod(RetainedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, UnplannedReferrerFingerprint));

            AssertRefusedNamingTheReferrer(result);
        }

        /// <summary>
        /// What: a body-only edit of a retained type an unchanged retained type names is refused
        /// the same way, because the edited declaration stays in the source and splits the type
        /// exactly as an added member does.
        /// </summary>
        [Test]
        public async Task Transform_MethodBodyEditedOnRetainedTypeReferencedByAnUnchangedRetainedTypesSignature_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerBodyEdit",
                RetainedSource,
                RetainedArtifactReferrerShape.PublicReturnType);
            File.WriteAllText(fixture.SourcePath, WithEditedComputeBody(RetainedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, UnplannedReferrerFingerprint));

            AssertRefusedNamingTheReferrer(result);
        }

        /// <summary>
        /// What: a referrer that only names the type in a private parameter still refuses the
        /// edit, because its bodies bind against the retained definition all the same.
        /// </summary>
        [Test]
        public async Task Transform_MethodAddedToRetainedTypeReferencedByAPrivateParameter_FailsRun()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerPrivateParameter",
                RetainedSource,
                RetainedArtifactReferrerShape.PrivateParameterType);
            File.WriteAllText(fixture.SourcePath, WithAddedMethod(RetainedSource));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, UnplannedReferrerFingerprint));

            AssertRefusedNamingTheReferrer(result);
        }

        /// <summary>
        /// What: a referrer whose unchanged declaration is part of the same run still refuses the
        /// edit, because an unchanged declaration leaves the source and binds from the retained
        /// assembly like one that was never edited.
        /// </summary>
        [Test]
        public async Task Transform_MethodAddedToRetainedTypeWhoseReferrerIsInTheRunUnchanged_FailsRun()
        {
            string source = WithReferrer(RetainedSource);
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerInRunUnchanged",
                source,
                RetainedArtifactReferrerShape.PublicReturnType);
            string referrerFingerprint =
                await fixture.ReadPlannedFingerprintAsync(HotReloadRetainedArtifactFixture.ReferrerMetadataName);
            File.WriteAllText(fixture.SourcePath, WithAddedMethod(source));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, referrerFingerprint));

            AssertRefusedNamingTheReferrer(result);
        }

        /// <summary>
        /// What: when the referrer is edited in the same run, both declarations stay in the source
        /// and bind to each other, so the added method is still emitted instead of the run being
        /// refused.
        /// </summary>
        [Test]
        public async Task Transform_MethodAddedToRetainedTypeWhoseReferrerIsEditedInTheSameRun_EmitsAnAddedMethod()
        {
            string source = WithReferrer(RetainedSource);
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerEditedInRun",
                source,
                RetainedArtifactReferrerShape.PublicReturnType);
            string referrerFingerprint =
                await fixture.ReadPlannedFingerprintAsync(HotReloadRetainedArtifactFixture.ReferrerMetadataName);
            File.WriteAllText(fixture.SourcePath, WithEditedMakeBody(WithAddedMethod(source)));

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, referrerFingerprint));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntries(result, RetainedTypeMetadataName, "Extra").Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a referenced retained type whose declaration is unchanged is not refused, because
        /// it leaves the source and binds from the retained assembly like its referrer.
        /// </summary>
        [Test]
        public async Task Transform_RetainedTypeReferencedByAnotherRetainedTypeButUnchanged_DoesNotFailTheRun()
        {
            HotReloadRetainedArtifactFixture fixture = await HotReloadRetainedArtifactFixture.CreateWithReferrerAsync(
                "ReferrerTargetUnchanged",
                RetainedSource,
                RetainedArtifactReferrerShape.PublicReturnType);

            TransformWorkerClientResult result = await RunAsync(
                fixture,
                fixture.CreateRecordedArtifactWithReferrer(fixture.RetainedFingerprint, UnplannedReferrerFingerprint));

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntries(result, RetainedTypeMetadataName, "Compute"), Is.Empty);
        }

        private static void AssertRefusedNamingTheReferrer(TransformWorkerClientResult result)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Output, Is.Null.Or.Property("entries").Empty);
            Assert.That(result.ErrorMessage, Does.Contain("'" + RetainedTypeMetadataName + "'"));
            Assert.That(
                result.ErrorMessage,
                Does.Contain("'" + HotReloadRetainedArtifactFixture.ReferrerMetadataName + "'"));
            Assert.That(result.ErrorMessage, Does.Contain("uloop compile"));
        }

        private static string WithAddedMethod(string source)
        {
            return source.Replace(ComputeMember, ComputeMember + "\n" + AddedMethodMember, StringComparison.Ordinal);
        }

        private static string WithEditedComputeBody(string source)
        {
            return source.Replace(ComputeBody, "            return Value * 3;\n", StringComparison.Ordinal);
        }

        // The referrer is appended inside the namespace, after the retained declaration.
        private static string WithReferrer(string source)
        {
            return source.Replace("    }\n}\n", "    }\n" + ReferrerDeclaration + "}\n", StringComparison.Ordinal);
        }

        private static string WithEditedMakeBody(string source)
        {
            return source.Replace(
                ReferrerMakeBody,
                "            Retained made = new Retained();\n            return made;\n",
                StringComparison.Ordinal);
        }

        private static async Task<TransformWorkerClientResult> RunAsync(
            HotReloadRetainedArtifactFixture fixture,
            TransformWorkerIntroducedTypeArtifactDto artifact)
        {
            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(
                fixture.BuildTransformInput(new[] { artifact }),
                CancellationToken.None);
        }

        private static TransformWorkerEntryDto[] FindEntries(
            TransformWorkerClientResult result,
            string typeMetadataName,
            string methodName)
        {
            List<TransformWorkerEntryDto> found = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName == typeMetadataName && entry.methodName == methodName)
                {
                    found.Add(entry);
                }
            }

            return found.ToArray();
        }
    }
}
