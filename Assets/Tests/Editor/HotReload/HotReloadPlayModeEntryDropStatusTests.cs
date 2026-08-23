using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies --status Message and DroppedByPlayModeEntryCount against leftover Play-entry drops.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropStatusTests
    {
        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
            HotReloadPatcher.RevertAll();
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: --status with no active changes and leftover identities uses the drop Message
        /// and serializes DroppedByPlayModeEntryCount.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_WhenNoActiveChangesAndDropsRemain_ReturnsExactDropMessage()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()", "Type.B()" });

            HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
            JObject json = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "0 change(s) currently active. 2 change(s) were discarded by the domain reload when Play Mode was entered — hot-reloaded edits that were never compiled are not in effect. Re-apply 'uloop hot-reload', or edit the files and run 'uloop compile'."));
            Assert.That(response.DroppedByPlayModeEntryCount, Is.EqualTo(2));
            Assert.That(response.ShouldSerializeDroppedByPlayModeEntryCount(), Is.True);
            Assert.That(json.Value<int>("DroppedByPlayModeEntryCount"), Is.EqualTo(2));
        }

        /// <summary>
        /// What: --status with no leftover identities omits DroppedByPlayModeEntryCount and
        /// keeps the active-count Message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_WhenNoDropsRemain_OmitsDroppedCountAndKeepsActiveMessage()
        {
            HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
            JObject json = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(response.Message, Is.EqualTo("0 change(s) currently active."));
            Assert.That(response.DroppedByPlayModeEntryCount, Is.EqualTo(0));
            Assert.That(response.ShouldSerializeDroppedByPlayModeEntryCount(), Is.False);
            Assert.That(json.Property("DroppedByPlayModeEntryCount"), Is.Null);
        }

        /// <summary>
        /// What: --status with one never-invoked active change and leftover identities
        /// keeps the existing active Message and still serializes DroppedByPlayModeEntryCount.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_Status_WhenActiveNeverInvokedAndDropsRemain_KeepsActiveMessageAndSerializesDroppedCount()
        {
            ApplyCoreFixtureTransplant();
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.Dropped()" });

            HotReloadResponse response = await ExecuteStatusAsync(CancellationToken.None);
            JObject json = JObject.Parse(
                JsonConvert.SerializeObject(
                    response,
                    Formatting.None,
                    UnityCliLoopJsonResponseSerializerSettings.Settings));

            Assert.That(
                response.Message,
                Is.EqualTo(
                    "1 change(s) currently active. 1 change(s) have not been invoked since their patch was applied; see Methods[].Reason."));
            Assert.That(response.DroppedByPlayModeEntryCount, Is.EqualTo(1));
            Assert.That(response.ShouldSerializeDroppedByPlayModeEntryCount(), Is.True);
            Assert.That(json.Value<int>("DroppedByPlayModeEntryCount"), Is.EqualTo(1));
        }

        private static async Task<HotReloadResponse> ExecuteStatusAsync(CancellationToken ct)
        {
            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                new JObject { ["Status"] = true },
                ct);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            Assert.That(response, Is.Not.Null);
            return response;
        }

        // Applies a handwritten transplant to ReplaceableCompute without invoking it.
        private static void ApplyCoreFixtureTransplant()
        {
            MethodInfo original = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.ReplaceableCompute),
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo shim = typeof(HotReloadHandwrittenShims).GetMethod(
                nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0),
                BindingFlags.Static | BindingFlags.Public);
            Assert.That(original, Is.Not.Null);
            Assert.That(shim, Is.Not.Null);

            HotReloadPatchResult applyResult = HotReloadPatcher.Apply(
                original,
                shim,
                HotReloadPatchShape.Transplant,
                "Assets/Tests/Fixture.cs");
            Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);
        }
    }
}
