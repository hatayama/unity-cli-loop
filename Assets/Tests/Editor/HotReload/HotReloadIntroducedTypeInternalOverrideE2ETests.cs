using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies referrers receive the internal-override refusal for compiled and retained bases.
    /// </summary>
    public sealed class HotReloadIntroducedTypeInternalOverrideE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string FixtureNamespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string BaseRequestedPath = "Assets/Tests/Editor/HotReload/UncompiledInternalOverrideBase.cs";
        private const string DerivedRequestedPath = "Assets/Tests/Editor/HotReload/UncompiledInternalOverrideOwner.cs";
        private const string CallerDeclarationAnchor =
            "        [MethodImpl(MethodImplOptions.NoInlining)]\n        public int Call(";
        private const string BaseDeclaration =
            "public class RetainedOverrideBase { internal virtual int Read() => 1; }";
        private const string DerivedDeclaration =
            "public class RefusedOverride : RetainedOverrideBase { internal override int Read() => 2; }";

        /// <summary>
        /// Verifies an actual referrer failure names the compiled-base override refusal and member.
        /// </summary>
        [Test]
        public async Task Run_CompiledInternalOverride_ReferrerNamesRefusal()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunReloadAsync(
                    DerivedRequestedPath, callerPath,
                    CreateEdits(DerivedRequestedPath,
                        "public class RefusedOverride : HotReloadInternalOverrideBase { internal override int Read() => 2; }",
                        callerPath, "RefusedOverride"));
                AssertReferrerNamesRefusal(result);
            });
        }

        /// <summary>
        /// Verifies metadata-only retained bases are refused before a new artifact is prepared.
        /// </summary>
        [Test]
        public Task Run_RetainedInternalOverrideWithoutOwner_ReportsRefusal()
        {
            return VerifyRetainedOverrideAsync(false);
        }

        /// <summary>
        /// Verifies re-reading a retained base declaration does not make it a same-batch new base.
        /// </summary>
        [Test]
        public Task Run_RetainedInternalOverrideWithOwner_ReportsRefusal()
        {
            return VerifyRetainedOverrideAsync(true);
        }

        private static async Task VerifyRetainedOverrideAsync(bool includeOwner)
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult first = await RunReloadAsync(
                    BaseRequestedPath, callerPath,
                    CreateEdits(BaseRequestedPath, BaseDeclaration, callerPath, "RetainedOverrideBase"));
                Assert.That(CountFailures(first), Is.Zero, DescribeOutcomes(first));
                HotReloadIntroducedTypeArtifact retained = readArtifact();
                Assert.That(retained, Is.Not.Null);
                int activeBefore = HotReloadCompositionRoot.Services.Domain.IntroducedTypes.ActiveTypeCount;

                string ownerPath = includeOwner ? BaseRequestedPath : DerivedRequestedPath;
                string declarations = includeOwner ? BaseDeclaration + DerivedDeclaration : DerivedDeclaration;
                HotReloadOrchestratorResult second = await RunReloadAsync(
                    ownerPath, callerPath, CreateEdits(ownerPath, declarations, callerPath, "RefusedOverride"));

                AssertReferrerNamesRefusal(second);
                Assert.That(readArtifact(), Is.SameAs(retained));
                Assert.That(HotReloadCompositionRoot.Services.Domain.IntroducedTypes.ActiveTypeCount,
                    Is.EqualTo(activeBefore));
            });
        }

        private static void AssertReferrerNamesRefusal(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Reason != null
                    && outcome.Reason.Contains("'RefusedOverride' was refused by this hot reload run (",
                        StringComparison.Ordinal))
                {
                    Assert.That(outcome.Reason, Does.Contain(
                        "Internal override in an introduced type requires a compile: "
                        + FixtureNamespace + ".RefusedOverride.Read"));
                    Assert.That(outcome.Reason, Does.Contain("run 'uloop compile'."));
                    return;
                }
            }

            Assert.Fail("A Failed referrer must name the refused override.\n" + DescribeOutcomes(result));
        }

        private static Dictionary<string, string> CreateEdits(
            string ownerPath, string declarations, string callerPath, string referredType)
        {
            string addedMember =
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int UseOverride() { return typeof(" + referredType + ").Name.Length; }\n\n";
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(callerSource, Does.Contain(CallerDeclarationAnchor));
            return new Dictionary<string, string>
            {
                [ownerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "OverrideOwner-" + Guid.NewGuid().ToString("N") + ".cs",
                    "namespace " + FixtureNamespace + " { " + declarations + " }"),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "OverrideReferrer-" + Guid.NewGuid().ToString("N") + ".cs",
                    callerSource.Replace(CallerDeclarationAnchor,
                        addedMember + CallerDeclarationAnchor, StringComparison.Ordinal))
            };
        }
    }
}
