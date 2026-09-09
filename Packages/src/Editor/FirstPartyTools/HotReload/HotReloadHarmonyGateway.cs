using System.Reflection;

using HarmonyLib;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The production <see cref="IHotReloadHarmony"/>: forwards straight to one Harmony instance.
    /// </summary>
    internal sealed class HotReloadHarmonyGateway : IHotReloadHarmony
    {
        private readonly Harmony _harmony;

        internal HotReloadHarmonyGateway(Harmony harmony)
        {
            Debug.Assert(harmony != null, "harmony must not be null.");
            _harmony = harmony;
        }

        public void Patch(MethodBase original, HarmonyMethod transpiler)
        {
            _harmony.Patch(original, transpiler: transpiler);
        }

        public void Unpatch(MethodBase original, HarmonyPatchType patchType, string harmonyId)
        {
            _harmony.Unpatch(original, patchType, harmonyId);
        }

        public void UnpatchAll(string harmonyId)
        {
            _harmony.UnpatchAll(harmonyId);
        }
    }
}
