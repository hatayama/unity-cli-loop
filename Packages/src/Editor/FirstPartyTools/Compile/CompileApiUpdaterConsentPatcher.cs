using System;
using System.Reflection;

using HarmonyLib;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Installs a Harmony prefix that declines Unity's Script Updating Consent dialog while a
    /// CLI compile is in flight.
    /// </summary>
    internal static class CompileApiUpdaterConsentPatcher
    {
        private static readonly Harmony HarmonyInstance =
            new Harmony(CompileApiUpdaterConsentConstants.HarmonyId);

        private static bool _installAttempted;

        /// <summary>
        /// Patches <see cref="EditorUtility.DisplayDialogComplex"/> once. Fail-open on install
        /// errors so a missing method or Harmony emit failure never blocks the Editor.
        /// </summary>
        internal static void Install()
        {
            if (_installAttempted)
            {
                return;
            }

            _installAttempted = true;

            MethodInfo original = typeof(EditorUtility).GetMethod(
                nameof(EditorUtility.DisplayDialogComplex),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) },
                modifiers: null);
            if (original == null)
            {
                VibeLogger.LogWarning(
                    "compile_api_updater_consent_patch_missing_method",
                    "Could not resolve EditorUtility.DisplayDialogComplex; Script Updating Consent decline is disabled.",
                    new { });
                return;
            }

            MethodInfo prefix = typeof(CompileApiUpdaterConsentPatcher).GetMethod(
                nameof(Prefix),
                BindingFlags.NonPublic | BindingFlags.Static);
            UnityEngine.Debug.Assert(prefix != null, "consent prefix must resolve");
            if (prefix == null)
            {
                VibeLogger.LogWarning(
                    "compile_api_updater_consent_patch_missing_prefix",
                    "Could not resolve the Script Updating Consent prefix; decline is disabled.",
                    new { });
                return;
            }

            try
            {
                // User-approved exception to the no-try-catch policy: Harmony emit/JIT
                // failures cannot be pre-validated, and an escaping exception during Editor
                // startup would disable the whole composition-root bootstrap. Log and fail
                // open so CLI compile still runs and the dialog appears as before.
                HarmonyInstance.Patch(original, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception exception)
            {
                VibeLogger.LogWarning(
                    "compile_api_updater_consent_patch_failed",
                    "Harmony failed to patch EditorUtility.DisplayDialogComplex; Script Updating Consent decline is disabled.",
                    new
                    {
                        exception_type = exception.GetType().Name,
                        exception_message = exception.Message
                    });
            }
        }

        // Why no try-catch: this prefix must stay exception-free so a failure cannot swallow
        // unrelated Editor dialogs. Decide and MarkDeclined only read/write static bools.
        // Why ref __result: Harmony's prefix contract for replacing the original return value.
        private static bool Prefix(string title, ref int __result)
        {
            (bool intercept, int declinedResult) decision = CompileApiUpdaterConsentDecision.Decide(
                CompileApiUpdaterConsentState.IsCliCompileInFlight,
                title);
            if (!decision.intercept)
            {
                return true;
            }

            __result = decision.declinedResult;
            CompileApiUpdaterConsentState.MarkDeclined();
            return false;
        }
    }
}
