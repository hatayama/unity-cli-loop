using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Finds C# files that should participate in package complexity analysis.
    /// </summary>
    public static class SourceFileCollector
    {
        public static SourceFileSet Collect(string rootPath)
        {
            string packageSourcePath = Path.Combine(rootPath, "Packages", "src");
            string assetsPath = Path.Combine(rootPath, "Assets");
            string testsPath = Path.Combine(rootPath, "tests");

            string[] productionFiles = CollectFiles(packageSourcePath);
            List<string> nonProductionFiles = new();
            nonProductionFiles.AddRange(CollectFiles(assetsPath));
            nonProductionFiles.AddRange(CollectFiles(testsPath));

            return new SourceFileSet(
                productionFiles,
                nonProductionFiles
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray());
        }

        private static string[] CollectFiles(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedSkillCopy(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsGeneratedSkillCopy(string path)
        {
            string normalized = path.Replace(Path.DirectorySeparatorChar, '/');
            return normalized.Contains("/.agents/", StringComparison.Ordinal)
                || normalized.Contains("/.claude/", StringComparison.Ordinal);
        }
    }
}
