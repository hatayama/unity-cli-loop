using System;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Starts a fresh wired-value restore report each time play mode is entered or left, so the
    /// report describes the scene reload that transition performs.
    /// </summary>
    /// <remarks>
    /// Why on the Exiting states: the scene reload, and with it the Awake reads that restore
    /// values, runs before the Entered state is raised, so a reset there would erase the restores
    /// it was meant to count.
    /// Why a named static handler: registering unsubscribes first, and a lambda would be a new
    /// delegate each time.
    /// </remarks>
    internal static class HotReloadWiredValueEditorHooks
    {
        /// <summary>
        /// Reads the persistence of the installed services when the callback fires, since a
        /// replacement scope may have installed other services since startup.
        /// </summary>
        internal static Func<HotReloadWiredValuePersistence> GetPersistence { get; set; }

        internal static void Initialize()
        {
            Debug.Assert(
                GetPersistence != null, "GetPersistence must be set before the hooks are registered.");
            EditorApplication.playModeStateChanged -= Handle;
            EditorApplication.playModeStateChanged += Handle;
        }

        internal static void Handle(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode && state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            GetPersistence().BeginSceneReloadSession();
        }
    }
}
