using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What resolving the external compiler found: the paths, or the components that are missing.
    /// </summary>
    internal sealed class ExternalCompilerPathResolution
    {
        private ExternalCompilerPathResolution(
            ExternalCompilerPaths paths,
            string editorPath,
            string contentsPath,
            IReadOnlyList<string> missingComponents)
        {
            Paths = paths;
            EditorPath = editorPath;
            ContentsPath = contentsPath;
            MissingComponents = missingComponents;
        }

        // Null unless every component exists.
        internal ExternalCompilerPaths Paths { get; }

        internal string EditorPath { get; }

        internal string ContentsPath { get; }

        // Empty unless some component is missing.
        internal IReadOnlyList<string> MissingComponents { get; }

        internal static ExternalCompilerPathResolution Found(ExternalCompilerPaths paths)
        {
            UnityEngine.Debug.Assert(paths != null, "paths must not be null.");
            return new ExternalCompilerPathResolution(paths, null, null, Array.Empty<string>());
        }

        internal static ExternalCompilerPathResolution Missing(
            string editorPath,
            string contentsPath,
            List<string> missingComponents)
        {
            UnityEngine.Debug.Assert(
                missingComponents != null && missingComponents.Count > 0,
                "missingComponents must list at least one component.");
            return new ExternalCompilerPathResolution(null, editorPath, contentsPath, missingComponents);
        }

        /// <summary>
        /// The Editor path or its contents path is empty: there is no layout to report on.
        /// </summary>
        internal static ExternalCompilerPathResolution NoEditorLayout()
        {
            return new ExternalCompilerPathResolution(null, null, null, Array.Empty<string>());
        }

        /// <summary>
        /// Reports the missing components the way a compile reports them, once per Editor path;
        /// does nothing when none is missing.
        /// </summary>
        internal void ReportMissingComponents()
        {
            if (MissingComponents.Count == 0)
            {
                return;
            }

            DynamicCompilationHealthMonitor.ReportFastPathUnavailable(
                EditorPath,
                ContentsPath,
                MissingComponents);
        }
    }
}
