using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

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
            internal bool PreflightCanProceed = true;
            internal int DisallowCallCount;
            internal int AllowCallCount;
            internal int RefreshCallCount;
            internal int PreflightCallCount;
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
                    () =>
                    {
                        PreflightCallCount++;
                        return (PreflightCanProceed, string.Empty, Array.Empty<string>());
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
            Assert.That(environment.PreflightCallCount, Is.EqualTo(1));
            Assert.That(result.Held, Is.False);
            Assert.That(result.ReleaseDeferred, Is.False);
            Assert.That(result.SceneRefreshWarning, Is.Null);
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

        /// <summary>
        /// What: a blocked scene preflight skips Refresh and returns the fixed warning.
        /// </summary>
        [Test]
        public void Sync_PreflightCannotProceed_SkipsRefreshAndReturnsWarning()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                IsFocused = true,
                IsPlaying = false,
                PreflightCanProceed = false
            };
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.AllowCallCount, Is.EqualTo(1));
            Assert.That(environment.RefreshCallCount, Is.EqualTo(0));
            Assert.That(environment.Held, Is.False);
            Assert.That(
                result.SceneRefreshWarning,
                Is.EqualTo(HotReloadAutoRefreshHoldConstants.SceneRefreshBlockedWarning));
        }

        /// <summary>
        /// What: a passing scene preflight calls Refresh once after release.
        /// </summary>
        [Test]
        public void Sync_PreflightCanProceed_RefreshesOnce()
        {
            FakeEnvironment environment = new FakeEnvironment { IsFocused = true, IsPlaying = false };
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);

            HotReloadAutoRefreshHoldSyncResult result = service.Sync(0);

            Assert.That(environment.PreflightCallCount, Is.EqualTo(1));
            Assert.That(environment.RefreshCallCount, Is.EqualTo(1));
            Assert.That(result.SceneRefreshWarning, Is.Null);
        }

        /// <summary>
        /// What: a Play-deferred release Flushes one Refresh when not playing and focused, then no-ops.
        /// </summary>
        [Test]
        public void FlushDeferredRefresh_AfterPlayRelease_RefreshesOnceThenNoOp()
        {
            FakeEnvironment environment = new FakeEnvironment { IsPlaying = true, IsFocused = true };
            HotReloadAutoRefreshHoldService service = environment.CreateService();
            service.Sync(1);
            HotReloadAutoRefreshHoldSyncResult deferred = service.Sync(0);
            Assert.That(deferred.ReleaseDeferred, Is.True);
            Assert.That(environment.RefreshCallCount, Is.EqualTo(0));

            environment.IsPlaying = false;
            HotReloadAutoRefreshHoldSyncResult flushed = service.FlushDeferredRefresh();

            Assert.That(environment.RefreshCallCount, Is.EqualTo(1));
            Assert.That(flushed.SceneRefreshWarning, Is.Null);

            service.FlushDeferredRefresh();
            Assert.That(environment.RefreshCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a stale held flag with an empty ledger lets ReconcileForTesting Allow once and clear the flag.
        /// </summary>
        [Test]
        public void ReconcileForTesting_StaleFlagWithEmptyLedger_AllowsOnceAndClearsFlag()
        {
            FakeEnvironment environment = new FakeEnvironment { Held = true };
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHoldService previous = HotReloadAutoRefreshHold.OverrideServiceForTesting;
            try
            {
                HotReloadAutoRefreshHold.OverrideServiceForTesting = environment.CreateService();

                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(environment.AllowCallCount, Is.EqualTo(1));
                Assert.That(environment.Held, Is.False);
            }
            finally
            {
                HotReloadAutoRefreshHold.OverrideServiceForTesting = previous;
                HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
            }
        }

        /// <summary>
        /// What: a throwing Allow during ReconcileForTesting leaves the flag held and the next reconcile releases.
        /// </summary>
        [Test]
        public void ReconcileForTesting_AllowThrowsOnce_SucceedsOnNextReconcile()
        {
            FakeEnvironment environment = new FakeEnvironment
            {
                Held = true,
                AllowException = new InvalidOperationException("reload")
            };
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHoldService previous = HotReloadAutoRefreshHold.OverrideServiceForTesting;
            try
            {
                HotReloadAutoRefreshHold.OverrideServiceForTesting = environment.CreateService();

                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(environment.AllowCallCount, Is.EqualTo(1));
                Assert.That(environment.Held, Is.True);

                environment.AllowException = null;
                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(environment.AllowCallCount, Is.EqualTo(2));
                Assert.That(environment.Held, Is.False);
            }
            finally
            {
                HotReloadAutoRefreshHold.OverrideServiceForTesting = previous;
                HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
            }
        }

        /// <summary>
        /// What: InitializeOnLoad already subscribed HotReloadAutoRefreshHold onto EditorApplication.update.
        /// </summary>
        [Test]
        public void Initialize_RegistersReconcileOnEditorApplicationUpdate()
        {
            Assert.That(
                HotReloadAutoRefreshHold.IsReconcileRegistered(),
                Is.True,
                "HotReloadAutoRefreshHold.Initialize must subscribe ReconcileOnUpdate.");
            Assert.That(
                EditorUpdateContainsHoldReconcile(),
                Is.True,
                "EditorApplication.update must include a HotReloadAutoRefreshHold delegate.");
        }

        // Why scan fields: EditorApplication.update is an event, so tests cannot call
        // GetInvocationList on the public accessor. The backing store is either a
        // Delegate or EventWithPerformanceTracker.
        private static bool EditorUpdateContainsHoldReconcile()
        {
            FieldInfo[] fields = typeof(EditorApplication).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                object value = fields[index].GetValue(null);
                Delegate current = value as Delegate;
                if (current != null && InvocationListContainsHold(current))
                {
                    return true;
                }

                if (EventTrackerContainsHold(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InvocationListContainsHold(Delegate current)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int index = 0; index < listeners.Length; index++)
            {
                if (listeners[index].Method.DeclaringType == typeof(HotReloadAutoRefreshHold)
                    && listeners[index].Method.Name == "ReconcileOnUpdate")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EventTrackerContainsHold(object source)
        {
            if (source == null)
            {
                return false;
            }

            string typeName = source.GetType().Name;
            if (typeName.IndexOf("EventWithPerformanceTracker", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            MethodInfo getEnumerator = source.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public);
            if (getEnumerator == null || getEnumerator.GetParameters().Length != 0)
            {
                return false;
            }

            object enumerator = getEnumerator.Invoke(source, null);
            if (enumerator == null)
            {
                return false;
            }

            MethodInfo moveNext = enumerator.GetType().GetMethod("MoveNext");
            PropertyInfo currentProperty = enumerator.GetType().GetProperty("Current");
            if (moveNext == null || currentProperty == null)
            {
                return false;
            }

            while ((bool)moveNext.Invoke(enumerator, null))
            {
                Delegate listener = currentProperty.GetValue(enumerator) as Delegate;
                if (listener != null
                    && listener.Method.DeclaringType == typeof(HotReloadAutoRefreshHold)
                    && listener.Method.Name == "ReconcileOnUpdate")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
