using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers what --status and --revert-all report about the types a hot reload introduced.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadIntroducedTypeStatusTests
    {
        private const string OwnerProjectRelativePath = "Assets/Tests/Fixture.cs";
        private const string OriginalAssemblyName = "Fixture.Assembly";
        private const string FirstMetadataName = "Fixture.IntroducedOne";
        private const string SecondMetadataName = "Fixture.IntroducedTwo";

        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// What: --status reports every active introduced type as its own row and totals the
        /// types, while the patch total keeps counting patched methods only.
        /// </summary>
        [Test]
        public void ExecuteStatus_WithActiveIntroducedTypes_ReportsOneRowPerTypeWithoutCountingThemAsPatches()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateArtifactWithTwoTypes();

                HotReloadResponse response = HotReloadStatusExecutor.ExecuteStatus();

                Assert.That(response.IntroducedTypes.Count, Is.EqualTo(2));
                Assert.That(response.ActiveIntroducedTypeTotal, Is.EqualTo(2));
                Assert.That(response.ActivePatchTotal, Is.EqualTo(0));
                HotReloadIntroducedTypeResult first = FindRow(response, FirstMetadataName);
                Assert.That(first.Kind, Is.EqualTo("Active"));
                Assert.That(first.AssemblyName, Is.EqualTo(OriginalAssemblyName));
                Assert.That(first.FilePath, Is.EqualTo(OwnerProjectRelativePath));
                Assert.That(FindRow(response, SecondMetadataName).Kind, Is.EqualTo("Active"));
            }
        }

        /// <summary>
        /// What: --status omits both type fields when this domain holds no introduced type.
        /// </summary>
        [Test]
        public void ExecuteStatus_WithNoActiveIntroducedType_OmitsBothTypeFields()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();

                HotReloadResponse response = HotReloadStatusExecutor.ExecuteStatus();

                Assert.That(response.IntroducedTypes.Count, Is.EqualTo(0));
                Assert.That(response.ShouldSerializeIntroducedTypes(), Is.False);
                Assert.That(response.ShouldSerializeActiveIntroducedTypeTotal(), Is.False);
            }
        }

        /// <summary>
        /// What: --revert-all says the introduced types it could not unload stay loaded until the
        /// next Domain Reload, and names each of them in a row so the caller can tell which types
        /// the revert left behind.
        /// </summary>
        [Test]
        public void ExecuteRevertAll_WithActiveIntroducedTypes_SaysTheyStayUntilTheNextDomainReload()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateArtifactWithTwoTypes();

                HotReloadResponse response = HotReloadStatusExecutor.ExecuteRevertAll();

                Assert.That(response.Message, Does.Contain("Domain Reload"));
                Assert.That(response.Message, Does.Contain("2 introduced type"));
                Assert.That(response.ActiveIntroducedTypeTotal, Is.EqualTo(2));
                Assert.That(
                    response.IntroducedTypes.Count,
                    Is.EqualTo(2),
                    "A total without the rows leaves the caller unable to name what stayed.");
                Assert.That(FindRow(response, FirstMetadataName).Kind, Is.EqualTo("Active"));
            }
        }

        /// <summary>
        /// What: --revert-all keeps its plain message when this domain holds no introduced type.
        /// </summary>
        [Test]
        public void ExecuteRevertAll_WithNoActiveIntroducedType_KeepsThePlainMessage()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();

                HotReloadResponse response = HotReloadStatusExecutor.ExecuteRevertAll();

                Assert.That(response.Message, Is.EqualTo("No active hot-reload changes to revert."));
                Assert.That(response.ShouldSerializeActiveIntroducedTypeTotal(), Is.False);
                Assert.That(response.ShouldSerializeIntroducedTypes(), Is.False);
            }
        }

        /// <summary>
        /// What: a refused apply warns that changes are still active when the only thing this
        /// domain holds is an introduced type, and still reports zero patches.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenAnIntroducedTypeIsActiveAndTheApplyIsRefused_WarnsThatChangesRemain()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateArtifactWithTwoTypes();

                HotReloadTool tool = new HotReloadTool();
                JObject parameters = new JObject
                {
                    ["Status"] = true,
                    ["Files"] = new JArray("Assets/Scripts/Player.cs")
                };

                UnityCliLoopToolResponse baseResponse =
                    await tool.ExecuteAsync(parameters, CancellationToken.None);
                HotReloadResponse response = baseResponse as HotReloadResponse;

                Assert.That(response, Is.Not.Null);
                Assert.That(response.Success, Is.False);
                Assert.That(response.ActivePatchTotal, Is.EqualTo(0), "Arrange: no method is patched.");
                Assert.That(
                    response.Message,
                    Does.EndWith("2 hot-reload change(s) are still active."),
                    "A refusal must not tell the caller the domain is clean while it holds two types.");
            }
        }

        private static void ActivateArtifactWithTwoTypes()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeStatusTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    CreateDescriptor(FirstMetadataName),
                    CreateDescriptor(SecondMetadataName)
                });
            HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
            HotReloadIntroducedTypeHolder.Registry.Activate(artifact);
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(string metadataName)
        {
            return new HotReloadIntroducedTypeDescriptor(
                OriginalAssemblyName,
                "original-mvid",
                metadataName,
                OwnerProjectRelativePath,
                "fingerprint-" + metadataName,
                "public class Introduced { }");
        }

        private static HotReloadIntroducedTypeResult FindRow(
            HotReloadResponse response,
            string typeName)
        {
            foreach (HotReloadIntroducedTypeResult row in response.IntroducedTypes)
            {
                if (row.TypeName == typeName)
                {
                    return row;
                }
            }

            Assert.Fail("The response carries no row for " + typeName + ".");
            return null;
        }
    }
}
