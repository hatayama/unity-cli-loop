using System;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Drives the Unity message forwarding from the editor's own callbacks: the update tick that
    /// keeps the proxies in step, and the two moments that have to take them off again.
    /// </summary>
    /// <remarks>
    /// Why named static handlers and not lambdas: every registration below unsubscribes first, and
    /// a lambda is a new delegate each time, so a second startup in one process would leave the
    /// previous one subscribed and tick twice.
    /// </remarks>
    internal static class HotReloadUnityMessageForwardingEditorHooks
    {
        /// <summary>
        /// Reads the forwarding of the installed services when a callback fires.
        /// </summary>
        /// <remarks>
        /// Why a provider and not a captured value: these callbacks take no argument and stay
        /// registered across a replacement scope, so an instance captured at startup would keep
        /// ticking a domain that is no longer installed.
        /// </remarks>
        internal static Func<HotReloadUnityMessageForwarding> GetForwarding { get; set; }

        internal static void Initialize()
        {
            Debug.Assert(
                GetForwarding != null, "GetForwarding must be set before the hooks are registered.");
            EditorApplication.update -= TickOnUpdate;
            EditorApplication.update += TickOnUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= ClearBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += ClearBeforeAssemblyReload;
            EditorApplication.playModeStateChanged -= Handle;
            EditorApplication.playModeStateChanged += Handle;
        }

        /// <summary>
        /// Suspends and empties on the way out of Play Mode, and resumes once Play Mode is running.
        /// </summary>
        /// <remarks>
        /// Why both on the way out: the editor keeps ticking between the request to leave and the
        /// moment it leaves, and a tick in that gap would attach proxies onto instances the stop is
        /// about to destroy. Suspending holds attaching back until Play Mode is genuinely running
        /// again, which is the only state where Unity delivers these messages at all.
        /// </remarks>
        internal static void Handle(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                HotReloadUnityMessageForwarding forwarding = GetForwarding();
                forwarding.Suspend();
                forwarding.Clear();
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                GetForwarding().Resume();
            }
        }

        // The tick is what notices an instance that appeared since the last one, so it runs on
        // every editor update rather than only when a hot reload applied something.
        private static void TickOnUpdate()
        {
            GetForwarding().Tick();
        }

        // The emitted proxy types and the shims they call belong to the domain that is about to be
        // discarded, so the components have to come off before Unity reloads over them.
        private static void ClearBeforeAssemblyReload()
        {
            GetForwarding().Clear();
        }
    }
}
