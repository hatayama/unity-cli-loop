#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates wire-visible responses for keyboard input simulation outcomes.
    /// </summary>
    internal static class KeyboardInputSimulationResponseFactory
    {
        // pressEdgeObserved stays nullable because KeyUp has no press edge to report;
        // Press/KeyDown must pass their observation so pause-point interruptions (the
        // most common E2E path) do not silently drop the field.
        // Why branch on action then pressWasApplied: KeyUp is a release, not a press
        // edge, so discarded/retry wording inverts the diagnosis. For Press/KeyDown,
        // Paused has two sources. (a) apply never completed — the queued edge never
        // reached the game. (b) WaitForPressLifetime after a successful apply — the
        // press already landed (including when that press itself fired the pause).
        // Claiming "discarded" in (b) inverts the diagnosis this message exists to
        // prevent. PressEdgeObserved is a separate observation and can stay false
        // even when pressWasApplied is true.
        internal static SimulateKeyboardResponse InterruptedKeyResult(
            UnityCliLoopKeyboardAction action,
            string keyName,
            bool? pressEdgeObserved,
            bool pressWasApplied)
        {
            SimulateKeyboardResponse result = new()
            {
                Success = true,
                Message = BuildInterruptedKeyMessage(action, keyName, pressWasApplied),
                Action = action.ToString(),
                KeyName = keyName,
                InterruptedByPausePoint = true,
                PressEdgeObserved = pressEdgeObserved,
                PressDeliveredToGame = action == UnityCliLoopKeyboardAction.KeyUp ? null : pressWasApplied
            };
            AttachPausePointHit(result);
            return result;
        }

        private static string BuildInterruptedKeyMessage(
            UnityCliLoopKeyboardAction action,
            string keyName,
            bool pressWasApplied)
        {
            if (action == UnityCliLoopKeyboardAction.KeyUp)
            {
                return
                    $"Keyboard input stopped because Unity paused during Pause Point inspection. Key '{keyName}' release was interrupted by the pause; the key is no longer held by Unity CLI Loop.";
            }

            if (pressWasApplied)
            {
                return
                    $"Keyboard input stopped because Unity paused during Pause Point inspection. Key '{keyName}' press was applied to the Input System in a gameplay update before the pause, so the game may already have consumed it; PressEdgeObserved says whether a gameplay update saw the press edge. Do not retry the press; re-check the affected state (and pause-point-status) before deciding the next step.";
            }

            return
                $"Keyboard input stopped because Unity paused during Pause Point inspection. Key '{keyName}' was released from Unity CLI Loop bookkeeping; the queued input edge was discarded before any gameplay update processed it, so the game never observed a press and it is safe to retry after resume.";
        }

        internal static SimulateKeyboardResponse AlreadyHeldRejection(string keyName, bool deviceIsPressed)
        {
            return new SimulateKeyboardResponse
            {
                Success = false,
                Message = $"Key '{keyName}' is already held down. Call KeyUp first.",
                Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                KeyName = keyName,
                KeyStateTrackedHeld = true,
                KeyStateDeviceIsPressed = deviceIsPressed
            };
        }

        internal static SimulateKeyboardResponse NotHeldRejection(string keyName, bool deviceIsPressed)
        {
            return new SimulateKeyboardResponse
            {
                Success = false,
                Message = $"Key '{keyName}' is not currently held. Call KeyDown first.",
                Action = UnityCliLoopKeyboardAction.KeyUp.ToString(),
                KeyName = keyName,
                KeyStateTrackedHeld = false,
                KeyStateDeviceIsPressed = deviceIsPressed
            };
        }

        internal static SimulateKeyboardResponse TimedOutKeyResult(
            UnityCliLoopKeyboardAction action,
            string keyName)
        {
            return new SimulateKeyboardResponse
            {
                Success = false,
                Message = $"Keyboard input timed out while waiting for Unity Editor update. Key '{keyName}' cleanup is queued for the next Editor tick.",
                Action = action.ToString(),
                KeyName = keyName
            };
        }

        private static void AttachPausePointHit(SimulateKeyboardResponse result)
        {
            if (result == null)
            {
                Debug.Assert(false, "result must not be null");
                return;
            }

            UloopPausePointSnapshot? snapshot = UloopPausePointRegistry.GetLatestHitSnapshot();
            if (snapshot == null)
            {
                return;
            }

            if (!snapshot.IsHit)
            {
                return;
            }

            string? snapshotId = snapshot.Id;
            if (string.IsNullOrEmpty(snapshotId))
            {
                return;
            }

            result.PausePointId = snapshotId;
            result.PausePointHitCount = snapshot.HitCount;
            result.PausePointHits = CollectPausePointHits();
        }

        // One input can hit several markers in the same frame; the representative
        // PausePointId alone forced agents into extra status calls to find the others.
        private static List<UnityCliLoopPausePointHit> CollectPausePointHits()
        {
            List<UnityCliLoopPausePointHit> hits = new();
            foreach (UloopPausePointSnapshot snapshot in UloopPausePointRegistry.GetHitSnapshots())
            {
                if (!snapshot.IsHit || string.IsNullOrEmpty(snapshot.Id))
                {
                    continue;
                }
                hits.Add(new UnityCliLoopPausePointHit
                {
                    Id = snapshot.Id,
                    HitCount = snapshot.HitCount
                });
            }
            return hits;
        }
    }
}
#endif
