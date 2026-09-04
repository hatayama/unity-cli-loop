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
            CompileErrorOrigin[] origins = new CompileErrorOrigin[errors.Length];
            for (int index = 0; index < errors.Length; index++)
            {
                messages[index] = errors[index].message;
                origins[index] = new CompileErrorOrigin(errors[index].message, errors[index].file);
            }

            string[] additions = CompileErrorNextActionsBuilder.Build(
                messages,
                CompileMissingReferenceAssemblyLookup.CreateLazyFinder());
            string[] assemblyDefinitionHints = CompileAssemblyDefinitionReferenceHintBuilder.Build(
                origins,
                CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath);
            response.NextActions = Append(Append(response.NextActions, additions), assemblyDefinitionHints);
        }

        private static string[] Append(string[] existing, string[] additions)
        {
            // Why: a null NextActions must stay null when nothing matched, so the JSON shape is unchanged.
            if (additions == null || additions.Length == 0)
            {
                return existing;
            }

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
