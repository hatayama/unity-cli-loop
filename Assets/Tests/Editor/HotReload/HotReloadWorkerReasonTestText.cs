using System;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Renders worker reasons the way the Editor does, so a test can assert on the sentence a
    /// user sees while the wire carries codes.
    /// </summary>
    internal static class HotReloadWorkerReasonTestText
    {
        /// <summary>
        /// The sentences of these reasons, in order.
        /// </summary>
        internal static string[] RenderAll(TransformWorkerReasonDto[] reasons)
        {
            if (reasons == null)
            {
                return Array.Empty<string>();
            }

            string[] rendered = new string[reasons.Length];
            for (int index = 0; index < reasons.Length; index++)
            {
                rendered[index] = HotReloadWorkerReasonText.Render(reasons[index]);
            }

            return rendered;
        }
    }
}
