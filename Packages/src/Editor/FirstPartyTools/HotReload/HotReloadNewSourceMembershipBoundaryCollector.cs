using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures matching disk and imported assembly-definition boundaries for one new source path.
    /// </summary>
    internal static class HotReloadNewSourceMembershipBoundaryCollector
    {
        private static readonly Regex MetaGuidRegex = new(
            "^guid:\\s*(?<guid>[0-9a-fA-F]+)\\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);
        internal static string TryCapture(
            string projectRoot,
            string projectRelativePath,
            out HotReloadNewSourceMembershipBoundary[] boundaries)
        {
            List<string> paths = CollectAncestorBoundaryPaths(projectRoot, projectRelativePath);
            AppendImportedAncestorBoundaryPaths(projectRoot, projectRelativePath, paths);
            AppendNearestAsmrefTargetPath(paths);
            List<HotReloadNewSourceMembershipBoundary> captured =
                new List<HotReloadNewSourceMembershipBoundary>(paths.Count);
            for (int index = 0; index < paths.Count; index++)
            {
                string boundaryPath = paths[index];
                string absolutePath = Path.Combine(projectRoot, boundaryPath);
                TextAsset imported = AssetDatabase.LoadAssetAtPath<TextAsset>(boundaryPath);
                byte[] diskBytes = File.Exists(absolutePath) ? File.ReadAllBytes(absolutePath) : null;
                byte[] importedBytes = imported?.bytes;
                string diskGuid = ReadMetaGuid(absolutePath + ".meta");
                string importedGuid = AssetDatabase.AssetPathToGUID(boundaryPath);
                string captureFailure = TryCreateBoundary(
                    boundaryPath,
                    diskBytes,
                    importedBytes,
                    diskGuid,
                    importedGuid,
                    out HotReloadNewSourceMembershipBoundary boundary);
                if (captureFailure != null)
                {
                    boundaries = null;
                    return captureFailure;
                }

                captured.Add(boundary);
            }

            boundaries = captured.ToArray();
            return null;
        }

        private static List<string> CollectAncestorBoundaryPaths(string projectRoot, string projectRelativePath)
        {
            List<string> paths = new List<string>();
            string directory = Path.GetDirectoryName(Path.Combine(projectRoot, projectRelativePath));
            string normalizedRoot = Path.GetFullPath(projectRoot);
            while (!string.IsNullOrEmpty(directory))
            {
                AppendBoundaryPaths(projectRoot, paths, Directory.GetFiles(directory, "*.asmref", SearchOption.TopDirectoryOnly));
                AppendBoundaryPaths(projectRoot, paths, Directory.GetFiles(directory, "*.asmdef", SearchOption.TopDirectoryOnly));
                if (string.Equals(Path.GetFullPath(directory), normalizedRoot, PathComparison))
                {
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return paths;
        }

        private static void AppendImportedAncestorBoundaryPaths(
            string projectRoot,
            string projectRelativePath,
            List<string> paths)
        {
            AppendImportedBoundaryPaths(
                projectRoot,
                projectRelativePath,
                paths,
                AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"));
            AppendImportedBoundaryPaths(
                projectRoot,
                projectRelativePath,
                paths,
                AssetDatabase.FindAssets("t:AssemblyDefinitionReferenceAsset"));
            paths.Sort(CompareBoundaryPaths);
        }

        private static void AppendNearestAsmrefTargetPath(List<string> paths)
        {
            string targetPath = ResolveNearestAsmrefTargetPath(paths, ResolveAsmrefTargetPath);
            if (!string.IsNullOrEmpty(targetPath) && !ContainsPath(paths, targetPath))
            {
                paths.Add(NormalizeProjectRelativePath(targetPath));
            }
        }

        internal static string ResolveNearestAsmrefTargetPath(
            IReadOnlyList<string> paths,
            Func<string, string> resolveAsmrefTargetPath)
        {
            Debug.Assert(paths != null, "paths must not be null.");
            Debug.Assert(resolveAsmrefTargetPath != null, "resolveAsmrefTargetPath must not be null.");
            if (paths.Count == 0 || !paths[0].EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return resolveAsmrefTargetPath(paths[0]);
        }

        private static string ResolveAsmrefTargetPath(string asmrefPath)
        {
            TextAsset asmref = AssetDatabase.LoadAssetAtPath<TextAsset>(asmrefPath);
            if (asmref == null)
            {
                return null;
            }

            string reference = HotReloadNewSourceMembershipValidator.ReadAsmrefReference(asmref.text);
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            if (reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.GUIDToAssetPath(reference.Substring("GUID:".Length));
            }

            return HotReloadNewSourceMembershipValidator.FindAsmdefPathByName(reference);
        }

        internal static string TryCreateBoundary(
            string projectRelativePath,
            byte[] diskBytes,
            byte[] importedBytes,
            string diskGuid,
            string importedGuid,
            out HotReloadNewSourceMembershipBoundary boundary)
        {
            boundary = null;
            if (diskBytes == null)
            {
                return "An imported assembly definition was deleted on disk. Compile the project and retry hot reload.";
            }

            if (importedBytes == null)
            {
                return "An assembly definition changed on disk but is not imported. Compile the project and retry hot reload.";
            }

            string diskContents = Convert.ToBase64String(diskBytes);
            string importedContents = Convert.ToBase64String(importedBytes);
            if (diskContents != importedContents
                || string.IsNullOrEmpty(diskGuid)
                || !string.Equals(diskGuid, importedGuid, StringComparison.OrdinalIgnoreCase))
            {
                return "An assembly definition changed on disk or its GUID differs from the imported asset. Compile the project and retry hot reload.";
            }

            boundary = new HotReloadNewSourceMembershipBoundary(
                projectRelativePath,
                diskContents,
                importedContents,
                diskGuid,
                importedGuid);
            return null;
        }

        private static void AppendImportedBoundaryPaths(
            string projectRoot,
            string projectRelativePath,
            List<string> paths,
            string[] guids)
        {
            for (int index = 0; index < guids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (!IsAncestorBoundaryPath(projectRelativePath, assetPath) || ContainsPath(paths, assetPath))
                {
                    continue;
                }

                paths.Add(NormalizeProjectRelativePath(assetPath));
            }
        }

        private static bool IsAncestorBoundaryPath(string projectRelativePath, string boundaryPath)
        {
            string sourceDirectory = Path.GetDirectoryName(NormalizeProjectRelativePath(projectRelativePath));
            string boundaryDirectory = Path.GetDirectoryName(NormalizeProjectRelativePath(boundaryPath));
            while (!string.IsNullOrEmpty(sourceDirectory))
            {
                if (string.Equals(sourceDirectory, boundaryDirectory, PathComparison))
                {
                    return true;
                }

                sourceDirectory = Path.GetDirectoryName(sourceDirectory);
            }

            return false;
        }

        private static bool ContainsPath(List<string> paths, string path)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                if (string.Equals(paths[index], path, PathComparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareBoundaryPaths(string left, string right)
        {
            int leftDepth = Path.GetDirectoryName(left)?.Split('/').Length ?? 0;
            int rightDepth = Path.GetDirectoryName(right)?.Split('/').Length ?? 0;
            int depthComparison = rightDepth.CompareTo(leftDepth);
            if (depthComparison != 0)
            {
                return depthComparison;
            }

            bool leftIsAsmref = left.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase);
            bool rightIsAsmref = right.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase);
            if (leftIsAsmref != rightIsAsmref)
            {
                return leftIsAsmref ? -1 : 1;
            }

            return string.Compare(left, right, PathComparison);
        }

        private static void AppendBoundaryPaths(string projectRoot, List<string> paths, string[] absolutePaths)
        {
            for (int index = 0; index < absolutePaths.Length; index++)
            {
                string relativePath = absolutePaths[index].Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                paths.Add(NormalizeProjectRelativePath(relativePath));
            }
        }

        private static string ReadMetaGuid(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return null;
            }

            Match match = MetaGuidRegex.Match(File.ReadAllText(metaPath));
            return match.Success ? match.Groups["guid"].Value : null;
        }

        private static string NormalizeProjectRelativePath(string path)
        {
            return string.IsNullOrEmpty(path) ? null : path.Replace('\\', '/');
        }

        private static StringComparison PathComparison => Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
