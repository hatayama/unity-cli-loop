#nullable enable
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pure unit tests for CLI PlayMode runInBackground override state transitions.
    /// </summary>
    public sealed class CliPlayModeRunInBackgroundControllerTests
    {
        [Test]
        public void OnCliPlayStarting_WhenInactive_SavesOriginalAndRequestsTrue()
        {
            // Verifies CLI Play start records the pre-Play value and forces runInBackground on.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);

            bool desired = controller.OnCliPlayStarting(currentRunInBackground: false);

            Assert.That(desired, Is.True);
            Assert.That(store.IsActive, Is.True);
            Assert.That(store.OriginalRunInBackground, Is.False);
        }

        [Test]
        public void OnCliPlayStarting_WhenAlreadyActive_DoesNotOverwriteOriginal()
        {
            // Verifies a second CLI Play while the override is active keeps the first original value.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: false);

            bool desired = controller.OnCliPlayStarting(currentRunInBackground: true);

            Assert.That(desired, Is.True);
            Assert.That(store.IsActive, Is.True);
            Assert.That(store.OriginalRunInBackground, Is.False);
        }

        [Test]
        public void PeekOriginalIfActive_WhenActive_ReturnsOriginalWithoutClearing()
        {
            // Verifies early exit restore can re-apply the original without dropping ownership yet.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: false);

            bool? peeked = controller.PeekOriginalIfActive();

            Assert.That(peeked, Is.False);
            Assert.That(store.IsActive, Is.True);
            Assert.That(store.OriginalRunInBackground, Is.False);
        }

        [Test]
        public void CommitRestoreAfterPlayModeExit_WhenActive_RestoresOriginalAndClears()
        {
            // Verifies stable EditMode exit (CLI Stop or manual Stop) restores and clears ownership.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: false);

            bool? restored = controller.CommitRestoreAfterPlayModeExit();

            Assert.That(restored, Is.False);
            Assert.That(store.IsActive, Is.False);
        }

        [Test]
        public void CommitRestoreAfterPlayModeExit_WhenInactive_ReturnsNull()
        {
            // Verifies manual Play sessions that never went through CLI Play leave runInBackground alone.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);

            bool? restored = controller.CommitRestoreAfterPlayModeExit();

            Assert.That(restored, Is.Null);
            Assert.That(store.IsActive, Is.False);
        }

        [Test]
        public void OnEditorStartup_WhenActiveAndStillPlaying_ReappliesTrue()
        {
            // Verifies domain reload during CLI Play re-applies runInBackground without clearing the original.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: false);

            bool? desired = controller.OnEditorStartup(isPlaying: true);

            Assert.That(desired, Is.True);
            Assert.That(store.IsActive, Is.True);
            Assert.That(store.OriginalRunInBackground, Is.False);
        }

        [Test]
        public void OnEditorStartup_WhenActiveButNotPlaying_RestoresOriginalAndClears()
        {
            // Verifies domain reload after Play exit still restores when ownership was not committed yet.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            store.Activate(originalRunInBackground: false);
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);

            bool? desired = controller.OnEditorStartup(isPlaying: false);

            Assert.That(desired, Is.False);
            Assert.That(store.IsActive, Is.False);
        }

        [Test]
        public void OnEditorStartup_WhenInactive_ReturnsNull()
        {
            // Verifies startup is a no-op when CLI never enabled a PlayMode override.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);

            bool? desired = controller.OnEditorStartup(isPlaying: true);

            Assert.That(desired, Is.Null);
        }

        [Test]
        public void CommitRestoreAfterPlayModeExit_WhenOriginalWasTrue_RestoresTrue()
        {
            // Verifies projects that already had runInBackground enabled stay enabled after CLI Play ends.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: true);

            bool? restored = controller.CommitRestoreAfterPlayModeExit();

            Assert.That(restored, Is.True);
            Assert.That(store.IsActive, Is.False);
        }

        [Test]
        public void ExitSequence_PeekThenCommit_SurvivesTransitionOverwriteModel()
        {
            // Verifies early peek restore + later commit matches the ExitingPlayMode → EnteredEditMode path.
            InMemoryCliPlayModeRunInBackgroundStore store = new InMemoryCliPlayModeRunInBackgroundStore();
            CliPlayModeRunInBackgroundController controller = new CliPlayModeRunInBackgroundController(store);
            controller.OnCliPlayStarting(currentRunInBackground: false);

            bool? peeked = controller.PeekOriginalIfActive();
            bool? committed = controller.CommitRestoreAfterPlayModeExit();

            Assert.That(peeked, Is.False);
            Assert.That(committed, Is.False);
            Assert.That(store.IsActive, Is.False);
            Assert.That(controller.PeekOriginalIfActive(), Is.Null);
            Assert.That(controller.CommitRestoreAfterPlayModeExit(), Is.Null);
        }

        private sealed class InMemoryCliPlayModeRunInBackgroundStore : ICliPlayModeRunInBackgroundStore
        {
            public bool IsActive { get; private set; }

            public bool OriginalRunInBackground { get; private set; }

            public void Activate(bool originalRunInBackground)
            {
                OriginalRunInBackground = originalRunInBackground;
                IsActive = true;
            }

            public void Clear()
            {
                IsActive = false;
                OriginalRunInBackground = false;
            }
        }
    }
}
