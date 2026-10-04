#nullable enable
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the service applies the controller's runInBackground decisions on CLI Play start and Play Mode exit,
    /// using an in-memory store and a recorded runInBackground value instead of SessionState and the real Application setting.
    /// </summary>
    public sealed class CliPlayModeRunInBackgroundServiceTests
    {
        private InMemoryStore _store = new();
        private bool _runInBackground;
        private int _setCount;
        private CliPlayModeRunInBackgroundService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryStore();
            _runInBackground = false;
            _setCount = 0;
            _service = new CliPlayModeRunInBackgroundService(
                new CliPlayModeRunInBackgroundController(_store),
                value =>
                {
                    _setCount++;
                    _runInBackground = value;
                },
                () => _runInBackground);
        }

        /// <summary>
        /// Verifies a CLI Play start records the current value as the original and turns runInBackground on.
        /// </summary>
        [Test]
        public void EnableForCliPlayStart_RecordsOriginalAndEnablesRunInBackground()
        {
            _service.EnableForCliPlayStart();

            Assert.That(_runInBackground, Is.True);
            Assert.That(_store.IsActive, Is.True);
            Assert.That(_store.OriginalRunInBackground, Is.False);
        }

        /// <summary>
        /// Verifies exiting Play Mode re-applies the original value but keeps the override active until Edit Mode is stable.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_ExitingPlayModeAfterCliStart_ReappliesOriginalAndKeepsOverride()
        {
            _service.EnableForCliPlayStart();

            _service.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(_runInBackground, Is.False);
            Assert.That(_store.IsActive, Is.True);
        }

        /// <summary>
        /// Verifies entering Edit Mode restores the original value and clears the override.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_EnteredEditModeAfterCliStart_RestoresOriginalAndClearsOverride()
        {
            _service.EnableForCliPlayStart();

            _service.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.That(_runInBackground, Is.False);
            Assert.That(_store.IsActive, Is.False);
        }

        /// <summary>
        /// Verifies Play Mode exit after a manual Play start never writes runInBackground.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_WithoutCliStart_DoesNotWriteRunInBackground()
        {
            _service.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            _service.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.That(_setCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies transitions other than exiting Play Mode and entering Edit Mode leave runInBackground and the override alone.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_EnteredPlayMode_LeavesOverrideUntouched()
        {
            _service.EnableForCliPlayStart();
            int setCountAfterStart = _setCount;

            _service.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            _service.OnPlayModeStateChanged(PlayModeStateChange.ExitingEditMode);

            Assert.That(_setCount, Is.EqualTo(setCountAfterStart));
            Assert.That(_runInBackground, Is.True);
            Assert.That(_store.IsActive, Is.True);
        }

        private sealed class InMemoryStore : ICliPlayModeRunInBackgroundStore
        {
            public bool IsActive { get; private set; }

            public bool OriginalRunInBackground { get; private set; }

            public void Activate(bool originalRunInBackground)
            {
                IsActive = true;
                OriginalRunInBackground = originalRunInBackground;
            }

            public void Clear()
            {
                IsActive = false;
                OriginalRunInBackground = false;
            }
        }
    }
}
