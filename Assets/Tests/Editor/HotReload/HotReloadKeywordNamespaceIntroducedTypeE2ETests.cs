using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a type an earlier reload introduced into a namespace with a
    /// keyword-named segment (@event): a later body-only edit is patched on the retained artifact,
    /// which is found by the type's metadata name.
    /// </summary>
    public class HotReloadKeywordNamespaceIntroducedTypeE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string FixtureFileName = "HotReloadKeywordNamespaceFixtures.cs";
        private const string HostTypeAnchor = "    public sealed class HotReloadKeywordNamespaceHost";
        private const string CallerBody = "return 36001;";
        private const string IntroducedTypeSimpleName = "HotReloadKeywordNamespaceIntroduced";

        // Why "event" without the escape: this is the name the compiled metadata carries.
        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.event." + IntroducedTypeSimpleName;

        /// <summary>
        /// What: editing only the body of a type that an earlier reload introduced into a
        /// keyword-named namespace patches it on the retained artifact without failures, and a call
        /// into the artifact runs the edited body.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeInKeywordNamedNamespaceBodyEdited_PatchesTheRetainedArtifact()
        {
            string fixturePath = FixturePath(FixtureFileName);

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    fixturePath,
                    "KeywordNamespaceIntroducing.cs",
                    "36003");
                Assert.That(CountFailures(first), Is.EqualTo(0), DescribeOutcomes(first));
                Assert.That(ReadComputedValue(readArtifact()), Is.EqualTo(36003));

                HotReloadOrchestratorResult second = await RunAsync(
                    fixturePath,
                    "KeywordNamespaceIntroducedBodyEdited.cs",
                    "36004");

                Assert.That(CountFailures(second), Is.EqualTo(0), DescribeOutcomes(second));
                Assert.That(
                    HasPatchedCompute(second),
                    Is.True,
                    "The edited body of the introduced type must be reported as patched.\n" + DescribeOutcomes(second));
                Assert.That(ReadComputedValue(readArtifact()), Is.EqualTo(36004));
            });
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(
            string fixturePath,
            string editedFileName,
            string computedValue)
        {
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(onDisk, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            Assert.That(onDisk, Does.Contain(CallerBody), "Precondition: caller body must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + computedValue + ";\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            string edited = onDisk
                .Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal)
                .Replace(
                    CallerBody,
                    "return new " + IntroducedTypeSimpleName + "().Compute();",
                    StringComparison.Ordinal);
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [fixturePath] = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited)
                });
        }

        private static bool HasPatchedCompute(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method != null
                    && outcome.Method.Contains("." + IntroducedTypeSimpleName + ".Compute()", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ReadComputedValue(HotReloadIntroducedTypeArtifact artifact)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to introduce the type before this check.");
            Type introducedType = artifact.Assembly.GetType(IntroducedTypeMetadataName, throwOnError: false);
            Assert.That(introducedType, Is.Not.Null, "The artifact must hold " + IntroducedTypeMetadataName + ".");
            MethodInfo compute = introducedType.GetMethod("Compute", BindingFlags.Instance | BindingFlags.Public);
            return (int)compute.Invoke(Activator.CreateInstance(introducedType), Array.Empty<object>());
        }
    }
}
