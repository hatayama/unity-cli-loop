#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates wire-visible responses for mouse input simulation outcomes.
    /// </summary>
    internal static class MouseInputSimulationResponseFactory
    {
        // Echoes the full conversion so callers can verify the Y-flip math against a
        // screenshot instead of trusting a hidden Screen.height-based flip.
        internal static SimulateMouseInputResponse SuccessButtonResult(
            UnityCliLoopMouseInputAction action,
            string message,
            string buttonName,
            Vector2 inputPos,
            GameViewCoordinateConversion conversion)
        {
            return new SimulateMouseInputResponse
            {
                Success = true,
                Message = message,
                Action = action.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y,
                InputCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_TOP_LEFT_GAME_VIEW,
                UnityCoordinateSystem = UnityCliLoopConstants.COORDINATE_SYSTEM_BOTTOM_LEFT_GAME_VIEW,
                GameViewWidth = conversion.GameViewSize.x,
                GameViewHeight = conversion.GameViewSize.y,
                InputPositionX = conversion.InputPosition.x,
                InputPositionY = conversion.InputPosition.y,
                InjectedUnityPositionX = conversion.InjectedUnityPosition.x,
                InjectedUnityPositionY = conversion.InjectedUnityPosition.y,
                CoordinateConversionFormula = UnityCliLoopConstants.COORDINATE_CONVERSION_FORMULA_GAME_VIEW_INPUT_TO_UNITY
            };
        }

        // Why branch on pressWasApplied: Paused has two sources. (a) TryDiscardForPause before
        // apply leaves pressWasApplied=false — the queued edge never reached the game.
        // (b) WaitForPressLifetime after a successful apply leaves pressWasApplied=true — the press
        // already landed (including when that press itself fired the pause point). Claiming
        // "discarded" in (b) inverts the diagnosis this message exists to prevent.
        // The verdict is definite: apply only completes inside an Input System update of the
        // configured gameplay type, so the game's polling in that frame observed the press. A
        // hedged "may have registered" left callers re-deriving this from world state.
        internal static SimulateMouseInputResponse InterruptedButtonResult(
            UnityCliLoopMouseInputAction action,
            string buttonName,
            Vector2 inputPos,
            bool pressWasApplied)
        {
            string message = pressWasApplied
                ? $"Mouse input stopped because Unity paused during Pause Point inspection. Button '{buttonName}' press was delivered to the game before the pause: the Input System processed the press edge in a gameplay update, so game code polling that frame observed it and the world state may already have changed. Do not retry the press; re-check the affected state (and pause-point-status) before deciding the next step."
                : $"Mouse input stopped because Unity paused during Pause Point inspection. Button '{buttonName}' was released from Unity CLI Loop bookkeeping; the queued input edge was discarded before any gameplay update processed it, so the game never observed a press and it is safe to retry after resume.";
            SimulateMouseInputResponse result = new()
            {
                Success = true,
                Message = message,
                Action = action.ToString(),
                Button = buttonName,
                PositionX = inputPos.x,
                PositionY = inputPos.y,
                InterruptedByPausePoint = true,
                PressDeliveredToGame = pressWasApplied
            };
            AttachPausePointHit(result);
            return result;
        }

        internal static SimulateMouseInputResponse InterruptedActionResult(
            UnityCliLoopMouseInputAction action)
        {
            SimulateMouseInputResponse result = new()
            {
                Success = true,
                Message = "Mouse input stopped because Unity paused during Pause Point inspection. Unity CLI Loop released its held input bookkeeping.",
                Action = action.ToString(),
                InterruptedByPausePoint = true
            };
            AttachPausePointHit(result);
            return result;
        }

        internal static SimulateMouseInputResponse TimedOutButtonResult(
            UnityCliLoopMouseInputAction action,
            string buttonName,
            Vector2 inputPos)
        {
            SimulateMouseInputResponse result = TimedOutActionResult(action);
            result.Button = buttonName;
            result.PositionX = inputPos.x;
            result.PositionY = inputPos.y;
            return result;
        }

        internal static SimulateMouseInputResponse TimedOutActionResult(
            UnityCliLoopMouseInputAction action)
        {
            return new SimulateMouseInputResponse
            {
                Success = false,
                Message = "Mouse input timed out while waiting for Unity Editor update. Cleanup is queued for the next Editor tick.",
                Action = action.ToString()
            };
        }

        private static void AttachPausePointHit(SimulateMouseInputResponse result)
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
