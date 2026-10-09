using System;
using System.Globalization;

using UnityEditor;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the time a compile started across that compile's domain reload to the snapshot
    /// capture that follows it. Every member reads or writes SessionState, so each call has to come
    /// from the Unity main thread.
    /// </summary>
    internal static class HotReloadCompileStartRecord
    {
        internal static void Initialize()
        {
            // Why unsubscribe first: Initialize runs once per domain, but a repeated registration
            // during tests must not stack handlers.
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
        }

        internal static void OnCompilationStarted(object context)
        {
            Record(DateTime.UtcNow.Ticks);
        }

        internal static void Record(long utcTicks)
        {
            SessionState.SetString(
                HotReloadConstants.CompileStartedUtcTicksSessionStateKey,
                utcTicks.ToString(CultureInfo.InvariantCulture));
        }

        internal static HotReloadCompileStart Read()
        {
            string text = SessionState.GetString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey, string.Empty);
            if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long utcTicks))
            {
                return HotReloadCompileStart.At(utcTicks);
            }

            return HotReloadCompileStart.Unknown;
        }

        internal static void Clear()
        {
            SessionState.EraseString(HotReloadConstants.CompileStartedUtcTicksSessionStateKey);
        }
    }
}
