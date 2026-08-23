using System.Diagnostics;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Appends error-origin NextActions onto a failed CompileResponse without replacing existing actions.
    /// </summary>
    internal static class CompileErrorNextActionsComposer
    {
        /// <summary>
        /// Why not replace NextActions: existing recovery steps such as API Updater consent must stay,
        /// and unmatched errors must remain fail-open.
        /// </summary>
        internal static void Apply(CompileResponse response, CompilerMessage[] errors)
        {
            Debug.Assert(response != null, "response must not be null");
            if (response.Success)
            {
                return;
            }

            if (errors == null)
            {
                return;
            }

            string[] messages = new string[errors.Length];
            for (int index = 0; index < errors.Length; index++)
            {
                messages[index] = errors[index].message;
            }

            string[] additions = CompileErrorNextActionsBuilder.Build(
                messages,
                CompileMissingReferenceAssemblyLookup.CreateLazyFinder());
            if (additions.Length == 0)
            {
                return;
            }

            response.NextActions = Append(response.NextActions, additions);
        }

        private static string[] Append(string[] existing, string[] additions)
        {
            if (existing == null || existing.Length == 0)
            {
                return additions;
            }

            string[] merged = new string[existing.Length + additions.Length];
            for (int index = 0; index < existing.Length; index++)
            {
                merged[index] = existing[index];
            }

            for (int index = 0; index < additions.Length; index++)
            {
                merged[existing.Length + index] = additions[index];
            }

            return merged;
        }
    }
}
