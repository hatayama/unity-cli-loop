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

            string[] productionFiles = CollectFiles(rootPath, packageSourcePath);
            List<string> nonProductionFiles = new();
            nonProductionFiles.AddRange(CollectFiles(rootPath, assetsPath));
            nonProductionFiles.AddRange(CollectFiles(rootPath, testsPath));

            return new SourceFileSet(
                productionFiles,
                nonProductionFiles
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray());
        }

        private static string[] CollectFiles(string rootPath, string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedSkillCopy(rootPath, path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        // Judged on the path relative to the scan root: a checkout that itself sits below a .claude or
        // .agents directory, such as a git worktree, would otherwise have every file skipped.
        private static bool IsGeneratedSkillCopy(string rootPath, string path)
        {
            string relativePath = Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');
            return relativePath.StartsWith(".agents/", StringComparison.Ordinal)
                || relativePath.Contains("/.agents/", StringComparison.Ordinal)
                || relativePath.StartsWith(".claude/", StringComparison.Ordinal)
                || relativePath.Contains("/.claude/", StringComparison.Ordinal);
        }
    }
}
