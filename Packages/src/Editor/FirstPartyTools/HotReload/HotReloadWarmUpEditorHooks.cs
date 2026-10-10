using System;

using UnityEditor;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stops the installed warm-up and the background reads of the compiled-caller analysis when
    /// the Editor is about to reload the domain or starts a compile: they would read dlls that are
    /// being rewritten, or work for a domain that is going away.
    /// </summary>
    internal static class HotReloadWarmUpEditorHooks
    {
        /// <summary>
        /// Reads the installed services when a hook fires.
        /// </summary>
        /// <remarks>
        /// Why a provider and not a captured value: the hooks take no services argument, and a
        /// replacement scope installs other services while the hooks stay registered.
        /// </remarks>
        internal static Func<HotReloadServices> GetServices { get; set; }

        internal static void Initialize()
        {
            // Why unsubscribe first: Initialize runs once per domain, but a repeated registration
            // during tests must not stack handlers.
            AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForReload;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            CompilationPipeline.compilationStarted -= ShutdownForCompile;
            CompilationPipeline.compilationStarted += ShutdownForCompile;
        }

        internal static void ShutdownForReload()
        {
            _ = GetServices().WarmUp.Shutdown(HotReloadConstants.WarmUpShutdownTriggerBeforeAssemblyReload);
            _ = GetServices().CompiledCallers.StopBackgroundReadsAsync(HotReloadConstants.WarmUpShutdownTriggerBeforeAssemblyReload);
        }

        internal static void ShutdownForCompile(object context)
        {
            _ = GetServices().WarmUp.Shutdown(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted);
            _ = GetServices().CompiledCallers.StopBackgroundReadsAsync(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted);
        }
    }
}
