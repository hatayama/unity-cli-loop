using System.Reflection;

using HarmonyLib;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The patch engine operations the hot-reload patcher performs, behind a seam a test can
    /// replace.
    /// </summary>
    /// <remarks>
    /// Why an interface rather than the Harmony instance: a rebuild failure cannot be provoked
    /// from outside Harmony, and the contained-failure contract of a revert has to be pinned by a
    /// test that makes exactly that call fail.
    /// </remarks>
    internal interface IHotReloadHarmony
    {
        /// <summary>
        /// Registers <paramref name="transpiler"/> on <paramref name="original"/>.
        /// </summary>
        void Patch(MethodBase original, HarmonyMethod transpiler);

        /// <summary>
        /// Removes the patch of one kind that <paramref name="harmonyId"/> owns from
        /// <paramref name="original"/>, rebuilding the method.
        /// </summary>
        void Unpatch(MethodBase original, HarmonyPatchType patchType, string harmonyId);

        /// <summary>
        /// Removes every patch <paramref name="harmonyId"/> owns.
        /// </summary>
        void UnpatchAll(string harmonyId);
    }
}
