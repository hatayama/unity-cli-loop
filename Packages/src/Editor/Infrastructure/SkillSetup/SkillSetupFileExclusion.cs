using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Identifies generated files that skill setup scans should ignore.
    /// </summary>
    internal static class SkillSetupFileExclusion
    {
        private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.Ordinal)
        {
            ".meta",
            ".DS_Store",
            ".gitkeep"
        };

        internal static bool IsExcludedSkillFile(string fileName)
        {
            if (ExcludedFileNames.Contains(fileName))
            {
                return true;
            }

            foreach (string excludedPattern in ExcludedFileNames)
            {
                if (fileName.EndsWith(excludedPattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
