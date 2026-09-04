using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the pure patch-count Auto Refresh hold policy with injected Unity actions.
    /// </summary>
    public sealed class HotReloadAutoRefreshHoldTests
    {
        private sealed class FakeEnvironment
        {
            internal bool Held;
            internal bool IsFocused = true;
            internal bool IsPlaying;
            internal int DisallowCallCount;
            internal int AllowCallCount;
            internal int RefreshCallCount;
            internal Exception DisallowException;
            internal Exception AllowException;
            internal readonly List<string> LoggedOperations = new List<string>();

            internal HotReloadAutoRefreshHoldService CreateService()
            {
                return new HotReloadAutoRefreshHoldService(
                    () => Held,
                    value => Held = value,
                    () => IsFocused,
                    () => IsPlaying,
                    () =>
                    {
                        DisallowCallCount++;
                        if (DisallowException != null)
                        {
                            throw DisallowException;
                        }
                    },
                    () =>
                    {
                        AllowCallCount++;
                        if (AllowException != null)
                        {
                            throw AllowException;
                        }
                    },
                    () => RefreshCallCount++,
                    (operation, message, context) => LoggedOperations.Add(operation),
                    (operation, message, context) => LoggedOperations.Add(operation));
            }
        }

        /// <summary>
        /// What: a 0→1 ledger rise calls Disallow once and sets the held flag.
        /// </summary>
        [Test]
        public void Sync_CountRisesFromZeroToOne_DisallowsOnceAndSetsFlag()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadAutoRefreshHoldService service = environment.CreateService();

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(1);

            Assert.That(environment.DisallowCallCount, Is.EqualTo(1));
            Assert.That(environment.Held, Is.True);
            Assert.That(result.Held, Is.True);
            Assert.That(result.NewlyArmed, Is.True);
            Assert.That(
                environment.LoggedOperations,
                Does.Contain(HotReloadAutoRefreshHoldConstants.VibeArmed));
        }

        /// <summary>
        /// What: a 1→2 rise while already held does not call Disallow again.
        /// </summary>
        [Test]
        public void Sync_CountRisesFromOneToTwo_DoesNotCallDisallowAgain()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(2);

            Assert.That(environment.DisallowCallCount, Is.EqualTo(1));
            Assert.That(environment.AllowCallCount, Is.EqualTo(0));
            Assert.That(environment.Held, Is.True);
            Assert.That(result.NewlyArmed, Is.False);
        }

        /// <summary>
        /// What: a 1→0 drop while focused and not playing Allows, clears the flag, and Refreshes once.
        /// </summary>
        [Test]
        public void Sync_CountFallsToZeroWhileFocusedAndNotPlaying_AllowsAndRefreshesOnce()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = true, IsPlaying = false };
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.AllowCallCount, Is.EqualTo(1));
            Assert.That(environment.Held, Is.False);
            Assert.That(environment.RefreshCallCount, Is.EqualTo(1));
            Assert.That(result.Held, Is.False);
            Assert.That(result.ReleaseDeferred, Is.False);
        }

        /// <summary>
        /// What: a 1→0 drop while playing Allows without Refresh and logs the deferred operation.
        /// </summary>
        [Test]
        public void Sync_CountFallsToZeroWhilePlaying_AllowsWithoutRefreshAndLogsDeferred()
        {
            FakeEnvironment environment = new FakeEnvironment { IsPlaying = true };
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.AllowCallCount, Is.EqualTo(1));
            Assert.That(environment.RefreshCallCount, Is.EqualTo(0));
            Assert.That(environment.Held, Is.False);
            Assert.That(result.ReleaseDeferred, Is.True);
            Assert.That(
                environment.LoggedOperations,
                Does.Contain(HotReloadAutoRefreshHoldConstants.VibeReleaseDeferred));
        }

        /// <summary>
        /// What: a throwing Disallow leaves the flag false, logs failed, and a later Sync can arm.
        /// </summary>
        [Test]
        public void Sync_DisallowThrows_LeavesFlagFalseAndRetriesSuccessfully()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                DisallowException = new InvalidOperationException("reload")
            };
            HotReloadAutoRefreshHoldService service = environment.CreateService();

            HotReloadAutoRefreshHoldSyncResult failed = service.Sync(1);

            Assert.That(environment.Held, Is.False);
            Assert.That(failed.Held, Is.False);
            Assert.That(
                environment.LoggedOperations,
                Does.Contain(HotReloadAutoRefreshHoldConstants.VibeFailed));

            environment.DisallowException = null;
            HotReloadAutoRefreshHoldSyncResult retried = service.Sync(1);

            Assert.That(environment.Held, Is.True);
            Assert.That(retried.Held, Is.True);
            Assert.That(retried.NewlyArmed, Is.True);
        }

        /// <summary>
        /// What: a throwing Allow leaves the flag true, logs release_failed, and a later Sync can release.
        /// </summary>
        [Test]
        public void Sync_AllowThrows_LeavesFlagTrueAndRetriesSuccessfully()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);
            environment.AllowException = new InvalidOperationException("reload");

            HotReloadAutoRefreshHoldSyncResult failed = service.Sync(0);

            Assert.That(environment.Held, Is.True);
            Assert.That(failed.Held, Is.True);
            Assert.That(
                environment.LoggedOperations,
                Does.Contain(HotReloadAutoRefreshHoldConstants.VibeReleaseFailed));

            environment.AllowException = null;
            HotReloadAutoRefreshHoldSyncResult retried = service.Sync(0);

            Assert.That(environment.Held, Is.False);
            Assert.That(retried.Held, Is.False);
        }

        /// <summary>
        /// What: Sync is a no-op for Disallow, Allow, and Refresh when the flag already matches the ledger.
        /// </summary>
        [Test]
        public void Sync_FlagMatchesLedger_CallsNothing()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);
            environment.DisallowCallCount = 0;
            environment.AllowCallCount = 0;
            environment.RefreshCallCount = 0;
            environment.LoggedOperations.Clear();

            service.Sync(1);
            service.Sync(3);

            Assert.That(environment.DisallowCallCount, Is.EqualTo(0));
            Assert.That(environment.AllowCallCount, Is.EqualTo(0));
            Assert.That(environment.RefreshCallCount, Is.EqualTo(0));
            Assert.That(environment.LoggedOperations, Is.Empty);
        }

        /// <summary>
        /// What: startup Sync with a stale held flag and an empty ledger releases the hold.
        /// </summary>
        [Test]
        public void Sync_StartupStaleFlagWithEmptyLedger_Releases()
        {
            FakeEnvironment environment = new FakeEnvironment { Held = true, IsFocused = true, IsPlaying = false };
            HotReloadAutoRefreshHoldService service = environment.CreateService();

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.AllowCallCount, Is.EqualTo(1));
            Assert.That(environment.Held, Is.False);
            Assert.That(result.Held, Is.False);
        }

        /// <summary>
        /// What: Sync with a clear flag and empty ledger calls no Unity Auto Refresh APIs.
        /// </summary>
        [Test]
        public void Sync_FlagFalseAndEmptyLedger_DoesNothing()
        {
            FakeEnvironment environment = new FakeEnvironment();
            HotReloadAutoRefreshHoldService service = environment.CreateService();

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.DisallowCallCount, Is.EqualTo(0));
            Assert.That(environment.AllowCallCount, Is.EqualTo(0));
            Assert.That(environment.RefreshCallCount, Is.EqualTo(0));
            Assert.That(environment.Held, Is.False);
            Assert.That(result.Held, Is.False);
            Assert.That(environment.LoggedOperations, Is.Empty);
        }
    }
}
