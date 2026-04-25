using System.Linq;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class ConnectedToolsMonitoringServiceTests
    {
        private McpEditorSettingsData _originalSettings;

        [SetUp]
        public void SetUp()
        {
            _originalSettings = CloneSettings(McpEditorSettings.GetSettings());
            ConnectedToolsMonitoringService.ResetStateForTests();
        }

        [TearDown]
        public void TearDown()
        {
            McpEditorSettings.SaveSettings(_originalSettings);
            McpEditorSettings.InvalidateCache();
            ConnectedToolsMonitoringService.ResetStateForTests();
        }

        [Test]
        public void ReplaceConnectedToolsForTests_ClearsStalePersistedTools_WhenNoLiveClients()
        {
            McpEditorSettings.SetConnectedLLMTools(new[]
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 3, 24, 22, 19, 27))
            });

            ConnectedToolsMonitoringService.ReplaceConnectedToolsForTests(System.Array.Empty<ConnectedClient>());

            Assert.That(ConnectedToolsMonitoringService.GetConnectedToolsAsClients(), Is.Empty);
            Assert.That(McpEditorSettings.GetConnectedLLMTools(), Is.Empty);
        }

        [Test]
        public void ReplaceConnectedToolsForTests_PersistsOnlyNamedLiveClients()
        {
            ConnectedClient[] liveClients =
            {
                new ConnectedClient("127.0.0.1:60001", null, 60001, "claude-code"),
                new ConnectedClient("127.0.0.1:60002", null, 60002, McpConstants.UNKNOWN_CLIENT_NAME)
            };

            ConnectedToolsMonitoringService.ReplaceConnectedToolsForTests(liveClients);

            ConnectedClient[] displayedTools = ConnectedToolsMonitoringService.GetConnectedToolsAsClients().ToArray();
            ConnectedLLMToolData[] persistedTools = McpEditorSettings.GetConnectedLLMTools();

            Assert.That(displayedTools, Has.Length.EqualTo(1));
            Assert.That(displayedTools[0].ClientName, Is.EqualTo("claude-code"));
            Assert.That(displayedTools[0].Endpoint, Is.EqualTo("127.0.0.1:60001"));

            Assert.That(persistedTools, Has.Length.EqualTo(1));
            Assert.That(persistedTools[0].Name, Is.EqualTo("claude-code"));
            Assert.That(persistedTools[0].Endpoint, Is.EqualTo("127.0.0.1:60001"));
        }

        [Test]
        public void ReplaceConnectedToolsForTests_ReplacesPreviousSnapshot_InsteadOfRestoringSettingsHistory()
        {
            McpEditorSettings.SetConnectedLLMTools(new[]
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 3, 24, 22, 19, 27))
            });

            ConnectedClient[] liveClients =
            {
                new ConnectedClient("127.0.0.1:60003", null, 60003, "cursor")
            };

            ConnectedToolsMonitoringService.ReplaceConnectedToolsForTests(liveClients);

            ConnectedClient[] displayedTools = ConnectedToolsMonitoringService.GetConnectedToolsAsClients().ToArray();
            ConnectedLLMToolData[] persistedTools = McpEditorSettings.GetConnectedLLMTools();

            Assert.That(displayedTools.Select(tool => tool.ClientName), Is.EquivalentTo(new[] { "cursor" }));
            Assert.That(persistedTools.Select(tool => tool.Name), Is.EquivalentTo(new[] { "cursor" }));
        }

        [Test]
        public void RestorePersistedConnectedToolsForTests_RehydratesReconnectSnapshot()
        {
            McpEditorSettings.SetConnectedLLMTools(new[]
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 3, 24, 22, 19, 27))
            });

            ConnectedToolsMonitoringService.RestorePersistedConnectedToolsForTests();

            ConnectedClient[] displayedTools = ConnectedToolsMonitoringService.GetConnectedToolsAsClients().ToArray();

            Assert.That(displayedTools, Has.Length.EqualTo(1));
            Assert.That(displayedTools[0].ClientName, Is.EqualTo("claude-code"));
            Assert.That(displayedTools[0].Endpoint, Is.EqualTo("127.0.0.1:58784"));
        }

        [Test]
        public void SaveConnectedToolsWhenChanged_WhenSnapshotMatches_SkipsPersistedWrite()
        {
            ConnectedLLMToolData[] incomingTools =
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 1, 2, 3))
            };
            ConnectedLLMToolData[] persistedTools =
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 1, 2, 3))
            };
            int saveCount = 0;

            bool saved = ConnectedToolsMonitoringService.SaveConnectedToolsWhenChanged(
                incomingTools,
                () => persistedTools,
                _ => saveCount++);

            Assert.That(saved, Is.False);
            Assert.That(saveCount, Is.EqualTo(0));
        }

        [Test]
        public void SaveConnectedToolsWhenChanged_WhenOnlyConnectedAtDiffers_SkipsPersistedWrite()
        {
            ConnectedLLMToolData[] incomingTools =
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 4, 5, 6))
            };
            ConnectedLLMToolData[] persistedTools =
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 1, 2, 3))
            };
            int saveCount = 0;

            bool saved = ConnectedToolsMonitoringService.SaveConnectedToolsWhenChanged(
                incomingTools,
                () => persistedTools,
                _ => saveCount++);

            Assert.That(saved, Is.False);
            Assert.That(saveCount, Is.EqualTo(0));
        }

        [Test]
        public void SaveConnectedToolsWhenChanged_WhenSnapshotDiffers_PersistsTools()
        {
            ConnectedLLMToolData[] incomingTools =
            {
                new ConnectedLLMToolData("claude-code", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 1, 2, 3))
            };
            ConnectedLLMToolData[] persistedTools =
            {
                new ConnectedLLMToolData("cursor", "127.0.0.1:58784", 58784, new System.DateTime(2026, 4, 24, 1, 2, 3))
            };
            int saveCount = 0;

            bool saved = ConnectedToolsMonitoringService.SaveConnectedToolsWhenChanged(
                incomingTools,
                () => persistedTools,
                _ => saveCount++);

            Assert.That(saved, Is.True);
            Assert.That(saveCount, Is.EqualTo(1));
        }

        private static McpEditorSettingsData CloneSettings(McpEditorSettingsData settings)
        {
            string json = UnityEngine.JsonUtility.ToJson(settings);
            return UnityEngine.JsonUtility.FromJson<McpEditorSettingsData>(json);
        }
    }
}
