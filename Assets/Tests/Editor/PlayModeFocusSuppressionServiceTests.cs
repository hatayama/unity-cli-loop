using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the pure focus-gated Play Mode window-raise suppression state machine.
    /// </summary>
    public sealed class PlayModeFocusSuppressionServiceTests
    {
        private sealed class FakeEnvironment
        {
            internal bool IsFocused;
            internal bool SuppressedFlag;
            internal int SuppressCallCount;
            internal int RestoreCallCount;
            internal int SuppressChangedViews;
            internal int RestoreChangedViews;
            internal readonly List<string> LoggedOperations = new List<string>();

            internal PlayModeFocusSuppressionService CreateService()
            {
                return new PlayModeFocusSuppressionService(
                    () => IsFocused,
                    () =>
                    {
                        SuppressCallCount++;
                        return SuppressChangedViews;
                    },
                    () =>
                    {
                        RestoreCallCount++;
                        return RestoreChangedViews;
                    },
                    () => SuppressedFlag,
                    value => SuppressedFlag = value,
                    (operation, message, context) => LoggedOperations.Add(operation));
            }
        }

        /// <summary>
        /// Verifies focus loss calls the suppress action and sets the persisted flag when views changed.
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusLostWithChangedViews_SetsFlagAndSuppresses()
        {
            FakeEnvironment environment = new FakeEnvironment { SuppressChangedViews = 1 };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(false);

            Assert.That(environment.SuppressCallCount, Is.EqualTo(1));
            Assert.That(environment.SuppressedFlag, Is.True);
        }

        /// <summary>
        /// Verifies focus loss leaves the flag clear when no view needed suppression.
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusLostWithNoChangedViews_LeavesFlagClear()
        {
            FakeEnvironment environment = new FakeEnvironment { SuppressChangedViews = 0 };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(false);

            Assert.That(environment.SuppressedFlag, Is.False);
        }

        /// <summary>
        /// Verifies focus loss keeps an already-set flag even when this call changed no views.
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusLostWithFlagAlreadySet_KeepsFlagSet()
        {
            FakeEnvironment environment = new FakeEnvironment { SuppressedFlag = true, SuppressChangedViews = 0 };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(false);

            Assert.That(environment.SuppressedFlag, Is.True);
        }

        /// <summary>
        /// Verifies focus gain restores views and clears the flag when the flag is set.
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusGainedWithFlagSet_RestoresAndClearsFlag()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                IsFocused = true,
                SuppressedFlag = true,
                RestoreChangedViews = 1
            };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(true);

            Assert.That(environment.RestoreCallCount, Is.EqualTo(1));
            Assert.That(environment.SuppressedFlag, Is.False);
        }

        /// <summary>
        /// Verifies focus gain never calls the restore action while the flag is clear.
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusGainedWithFlagClear_DoesNotCallRestore()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = true };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(true);

            Assert.That(environment.RestoreCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies focus gain clears the flag even when restore changed no views (views were closed).
        /// </summary>
        [Test]
        public void HandleFocusChanged_FocusGainedWithFlagSetAndNoViews_StillClearsFlag()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                IsFocused = true,
                SuppressedFlag = true,
                RestoreChangedViews = 0
            };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.HandleFocusChanged(true);

            Assert.That(environment.SuppressedFlag, Is.False);
            Assert.That(environment.LoggedOperations, Does.Contain("play_focus_suppress_released"));
        }

        /// <summary>
        /// Verifies reconcile arms suppression while the Editor is unfocused (no focus-lost event needed).
        /// </summary>
        [Test]
        public void Reconcile_WhileUnfocused_ArmsSuppression()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = false, SuppressChangedViews = 2 };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.Reconcile();

            Assert.That(environment.SuppressCallCount, Is.EqualTo(1));
            Assert.That(environment.SuppressedFlag, Is.True);
        }

        /// <summary>
        /// Verifies reconcile restores views while focused when a stale flag survived a restart.
        /// </summary>
        [Test]
        public void Reconcile_WhileFocusedWithStaleFlag_RestoresAndClearsFlag()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                IsFocused = true,
                SuppressedFlag = true,
                RestoreChangedViews = 1
            };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.Reconcile();

            Assert.That(environment.RestoreCallCount, Is.EqualTo(1));
            Assert.That(environment.SuppressedFlag, Is.False);
        }

        /// <summary>
        /// Verifies reconcile is a no-op while focused with a clear flag (the steady state).
        /// </summary>
        [Test]
        public void Reconcile_WhileFocusedWithFlagClear_DoesNothing()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = true };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.Reconcile();

            Assert.That(environment.SuppressCallCount, Is.EqualTo(0));
            Assert.That(environment.RestoreCallCount, Is.EqualTo(0));
            Assert.That(environment.LoggedOperations, Is.Empty);
        }

        /// <summary>
        /// Verifies the flag round-trips through the injected store across service instances,
        /// simulating a domain reload between suppress and restore.
        /// </summary>
        [Test]
        public void SuppressedFlag_PersistsAcrossServiceInstances_ViaInjectedStore()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = false, SuppressChangedViews = 1 };
            PlayModeFocusSuppressionService firstService = environment.CreateService();
            firstService.HandleFocusChanged(false);
            Assert.That(environment.SuppressedFlag, Is.True);

            environment.IsFocused = true;
            environment.RestoreChangedViews = 1;
            PlayModeFocusSuppressionService secondService = environment.CreateService();
            secondService.HandleFocusChanged(true);

            Assert.That(environment.RestoreCallCount, Is.EqualTo(1));
            Assert.That(environment.SuppressedFlag, Is.False);
        }

        /// <summary>
        /// Verifies repeated reconcile while unfocused logs the armed operation only when views actually changed.
        /// </summary>
        [Test]
        public void Reconcile_RepeatedWhileUnfocused_LogsArmedOnlyOnActualChange()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = false, SuppressChangedViews = 1 };
            PlayModeFocusSuppressionService service = environment.CreateService();

            service.Reconcile();
            environment.SuppressChangedViews = 0;
            service.Reconcile();
            service.Reconcile();

            Assert.That(environment.SuppressCallCount, Is.EqualTo(3));
            Assert.That(
                environment.LoggedOperations.FindAll(operation => operation == "play_focus_suppress_armed"),
                Has.Count.EqualTo(1));
        }
    }
}
