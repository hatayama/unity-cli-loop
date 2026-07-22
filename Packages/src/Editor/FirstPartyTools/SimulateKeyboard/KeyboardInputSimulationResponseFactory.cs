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
        internal static SimulateKeyboardResponse InterruptedKeyResult(
            UnityCliLoopKeyboardAction action,
            string keyName,
            bool? pressEdgeObserved)
        {
            SimulateKeyboardResponse result = new()
            {
                Success = true,
                Message = $"Keyboard input stopped because Unity paused during Pause Point inspection. Key '{keyName}' was released from Unity CLI Loop bookkeeping.",
                Action = action.ToString(),
                KeyName = keyName,
                InterruptedByPausePoint = true,
                PressEdgeObserved = pressEdgeObserved
            };
            AttachPausePointHit(result);
            return result;
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
