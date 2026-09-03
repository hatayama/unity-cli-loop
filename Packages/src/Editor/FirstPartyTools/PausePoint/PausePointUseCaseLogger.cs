using System;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Emits the structured log entries for pause point enable, clear, and expiry events.
    /// </summary>
    internal static class PausePointUseCaseLogger
    {
        // Why: PausePointStatusBridgeCommand duplicates this instead of sharing it, since that
        // bridge must not reference this Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes.
        internal static void LogCleared(string target, string statusBeforeClear)
        {
            VibeLogger.LogInfo(
                "pause_point_cleared",
                $"Pause point cleared: {target}",
                new { Target = target, StatusBeforeClear = statusBeforeClear });
        }

        // Why: PausePointStatusBridgeCommand duplicates this instead of sharing it, since that
        // bridge must not reference this Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes.
        internal static void LogExpired(string id, long elapsedSinceEnabledMilliseconds)
        {
            VibeLogger.LogInfo(
                "pause_point_expired",
                $"Pause point expired before being cleared: {id}",
                new { Id = id, ElapsedSinceEnabledMilliseconds = elapsedSinceEnabledMilliseconds });
        }

        internal static void LogEnable(string id, string resolvedMethod, string fileLine, string mode, string warning)
        {
            VibeLogger.LogInfo(
                "pause_point_enable",
                $"Pause point enabled: {id}",
                new { Id = id, ResolvedMethod = resolvedMethod, FileLine = fileLine, Mode = mode, HasWarning = !string.IsNullOrEmpty(warning) });
        }

        // Captures the state needed to diagnose a physics-callback dispatch miss if one recurs:
        // whether Play Mode is running, how long the current domain has been alive without a
        // reload (a suspected factor -- see docs/regression-harness.md), the declaring type, and
        // (for MonoBehaviour-derived types only) how many instances currently exist in the loaded
        // scenes. statusBeforeClear is empty at enable time (no clear has happened yet) and
        // Enabled/Expired at clear time.
        internal static void LogPhysicsDispatchDiagnostics(string operation, string id, Type declaringType, string statusBeforeClear)
        {
            // Only reachable via PausePointUseCase.PhysicsFlaggedDeclaringTypesById, which is populated solely from
            // a successful patch's method.DeclaringType -- a C#-sourced method always has one.
            Debug.Assert(declaringType != null, "declaringType must not be null");

            bool isMonoBehaviourDerived = typeof(MonoBehaviour).IsAssignableFrom(declaringType);
            // -1 signals "not applicable": counting instances only means something when the
            // declaring type is a MonoBehaviour (the physics dispatch miss this diagnostic exists
            // for is scoped to MonoBehaviour physics message methods).
#if UNITY_6000_4_OR_NEWER
            int instanceCount = isMonoBehaviourDerived
                ? UnityEngine.Object.FindObjectsByType(declaringType, FindObjectsInactive.Include).Length
                : -1;
#else
            int instanceCount = isMonoBehaviourDerived
                ? UnityEngine.Object.FindObjectsByType(declaringType, FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                : -1;
#endif

            VibeLogger.LogInfo(
                operation,
                $"Physics-callback pause point dispatch diagnostics: {id}",
                new
                {
                    Id = id,
                    IsPlaying = EditorApplication.isPlaying,
                    IsPaused = EditorApplication.isPaused,
                    SecondsSinceLastDomainReload = PausePointDomainReloadTracker.SecondsSinceLoad(),
                    DeclaringType = declaringType.FullName,
                    InstanceCount = instanceCount,
                    StatusBeforeClear = statusBeforeClear
                });
        }
    }
}
