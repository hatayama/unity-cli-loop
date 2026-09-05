using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Immutable evidence that a source absent from the last compiled source list belongs to an unchanged assembly.
    /// </summary>
    internal sealed class HotReloadNewSourceMembershipEvidence
    {
        internal HotReloadNewSourceMembershipEvidence(
            string projectRelativePath,
            string assemblyName,
            string targetDllPath,
            string targetDllMvid,
            string resolvedAssemblyDefinitionPath,
            HotReloadNewSourceMembershipBoundary[] boundaries)
        {
            ProjectRelativePath = projectRelativePath;
            AssemblyName = assemblyName;
            TargetDllPath = targetDllPath;
            TargetDllMvid = targetDllMvid;
            ResolvedAssemblyDefinitionPath = resolvedAssemblyDefinitionPath;
            Boundaries = boundaries;
        }

        internal string ProjectRelativePath { get; }

        internal string AssemblyName { get; }

        internal string TargetDllPath { get; }

        internal string TargetDllMvid { get; }

        internal string ResolvedAssemblyDefinitionPath { get; }

        internal HotReloadNewSourceMembershipBoundary[] Boundaries { get; }
    }

    /// <summary>
    /// One disk/import assembly boundary captured for a new source membership decision.
    /// </summary>
    internal sealed class HotReloadNewSourceMembershipBoundary
    {
        internal HotReloadNewSourceMembershipBoundary(
            string projectRelativePath,
            string diskContents,
            string importedContents,
            string diskGuid,
            string importedGuid)
        {
            ProjectRelativePath = projectRelativePath;
            DiskContents = diskContents;
            ImportedContents = importedContents;
            DiskGuid = diskGuid;
            ImportedGuid = importedGuid;
        }

        internal string ProjectRelativePath { get; }

        internal string DiskContents { get; }

        internal string ImportedContents { get; }

        internal string DiskGuid { get; }

        internal string ImportedGuid { get; }
    }

    /// <summary>
    /// Captures and rechecks the assembly membership evidence required before a new source can enter hot reload.
    /// </summary>
    internal static class HotReloadNewSourceMembershipValidator
    {
        [Serializable]
        private sealed class AssemblyDefinitionJson
        {
            public string name;
        }

        [Serializable]
        private sealed class AssemblyReferenceJson
        {
            public string reference;
        }

        internal static string TryCapture(
            string projectRoot,
            string projectRelativePath,
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            out HotReloadNewSourceMembershipEvidence evidence)
        {
            evidence = null;
            string notReadyReason = HotReloadEditorStateSnapshotProvider.GetNotReadyReason(
                HotReloadEditorStateSnapshotProvider.CaptureCurrent());
            if (notReadyReason != null)
            {
                return notReadyReason;
            }

            string captureFailure = HotReloadNewSourceMembershipBoundaryCollector.TryCapture(
                projectRoot,
                projectRelativePath,
                out HotReloadNewSourceMembershipBoundary[] boundaries);
            if (captureFailure != null)
            {
                return captureFailure;
            }

            string resolvedAssemblyDefinitionPath =
                CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(projectRelativePath);
            string resolutionFailure = ValidateResolvedAssemblyDefinition(
                projectRelativePath,
                assemblyName,
                compilationAssembly,
                boundaries,
                resolvedAssemblyDefinitionPath);
            if (resolutionFailure != null)
            {
                return resolutionFailure;
            }

            evidence = new HotReloadNewSourceMembershipEvidence(
                NormalizeProjectRelativePath(projectRelativePath),
                assemblyName,
                Path.GetFullPath(targetDllPath),
                ReadMvid(targetDllPath),
                NormalizeProjectRelativePath(resolvedAssemblyDefinitionPath),
                boundaries);
            return null;
        }

        internal static string TryRevalidate(HotReloadNewSourceMembershipEvidence evidence)
        {
            Debug.Assert(evidence != null, "evidence must not be null.");
            string notReadyReason = HotReloadEditorStateSnapshotProvider.GetNotReadyReason(
                HotReloadEditorStateSnapshotProvider.CaptureCurrent());
            if (notReadyReason != null)
            {
                return notReadyReason;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(evidence.AssemblyName);
            if (compilationAssembly == null)
            {
                return "The resolved assembly is no longer present in the compilation pipeline. Compile the project and retry hot reload.";
            }

            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                evidence.AssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            if (!string.Equals(Path.GetFullPath(targetDllPath), evidence.TargetDllPath, StringComparison.Ordinal)
                || !File.Exists(targetDllPath)
                || !string.Equals(ReadMvid(targetDllPath), evidence.TargetDllMvid, StringComparison.Ordinal)
                || HotReloadPatchTargetSupport.CheckMvidGuard(evidence.AssemblyName, targetDllPath) != null)
            {
                return "The compiled assembly changed while hot reload was preparing. Compile the project and retry hot reload.";
            }

            string captureFailure = HotReloadNewSourceMembershipBoundaryCollector.TryCapture(
                projectRoot,
                evidence.ProjectRelativePath,
                out HotReloadNewSourceMembershipBoundary[] currentBoundaries);
            if (captureFailure != null)
            {
                return captureFailure;
            }

            string resolvedAssemblyDefinitionPath =
                CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(evidence.ProjectRelativePath);
            string resolutionFailure = ValidateResolvedAssemblyDefinition(
                evidence.ProjectRelativePath,
                evidence.AssemblyName,
                compilationAssembly,
                currentBoundaries,
                resolvedAssemblyDefinitionPath);
            if (resolutionFailure != null)
            {
                return resolutionFailure;
            }

            HotReloadNewSourceMembershipEvidence currentEvidence = new HotReloadNewSourceMembershipEvidence(
                NormalizeProjectRelativePath(evidence.ProjectRelativePath),
                evidence.AssemblyName,
                Path.GetFullPath(targetDllPath),
                ReadMvid(targetDllPath),
                NormalizeProjectRelativePath(resolvedAssemblyDefinitionPath),
                currentBoundaries);
            if (!EvidenceMatches(evidence, currentEvidence))
            {
                return "Assembly definition membership changed while hot reload was preparing. Compile the project and retry hot reload.";
            }

            return null;
        }

        internal static string TryRevalidateFiles(IReadOnlyList<HotReloadGroupFile> files)
        {
            Debug.Assert(files != null && files.Count > 0, "files must not be empty.");
            for (int index = 0; index < files.Count; index++)
            {
                HotReloadNewSourceMembershipEvidence evidence = files[index].NewSourceMembershipEvidence;
                if (evidence == null)
                {
                    continue;
                }

                string failure = TryRevalidate(evidence);
                if (failure != null)
                {
                    return failure;
                }
            }

            return null;
        }

        private static string ValidateResolvedAssemblyDefinition(
            string projectRelativePath,
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            HotReloadNewSourceMembershipBoundary[] boundaries,
            string resolvedAssemblyDefinitionPath)
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativePath);
            if (string.IsNullOrEmpty(rawAssemblyName)
                || !string.Equals(Path.GetFileNameWithoutExtension(rawAssemblyName), assemblyName, StringComparison.Ordinal)
                || !string.Equals(compilationAssembly.name, assemblyName, StringComparison.Ordinal))
            {
                return "Unity resolved the new source to a different assembly. Compile the project and retry hot reload.";
            }

            string expectedAssemblyDefinitionPath = ResolveNearestBoundaryTarget(
                boundaries,
                AssetDatabase.GUIDToAssetPath,
                FindAsmdefPathByName);
            if (!string.Equals(
                    NormalizeProjectRelativePath(expectedAssemblyDefinitionPath),
                    NormalizeProjectRelativePath(resolvedAssemblyDefinitionPath),
                    StringComparison.Ordinal))
            {
                return "The imported assembly definition does not match the new source boundary. Compile the project and retry hot reload.";
            }

            return null;
        }

        internal static string ResolveNearestBoundaryTarget(
            HotReloadNewSourceMembershipBoundary[] boundaries,
            Func<string, string> resolveGuidToAssetPath,
            Func<string, string> findAsmdefPathByName)
        {
            Debug.Assert(resolveGuidToAssetPath != null, "resolveGuidToAssetPath must not be null.");
            Debug.Assert(findAsmdefPathByName != null, "findAsmdefPathByName must not be null.");
            if (boundaries.Length == 0)
            {
                return null;
            }

            HotReloadNewSourceMembershipBoundary nearest = boundaries[0];
            if (nearest.ProjectRelativePath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                return nearest.ProjectRelativePath;
            }

            string reference = ReadAsmrefReference(
                System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(nearest.DiskContents)));
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            if (reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                return resolveGuidToAssetPath(reference.Substring("GUID:".Length));
            }

            return findAsmdefPathByName(reference);
        }

        internal static string ReadAssemblyDefinitionName(string contents)
        {
            AssemblyDefinitionJson asmdef = JsonUtility.FromJson<AssemblyDefinitionJson>(contents);
            return asmdef?.name;
        }

        internal static string ReadAsmrefReference(string contents)
        {
            AssemblyReferenceJson asmref = JsonUtility.FromJson<AssemblyReferenceJson>(contents);
            return asmref?.reference;
        }

        internal static string FindAsmdefPathByName(string assemblyName)
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            for (int index = 0; index < asmdefGuids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(asmdefGuids[index]);
                TextAsset asmdef = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (asmdef != null
                    && string.Equals(ReadAssemblyDefinitionName(asmdef.text), assemblyName, StringComparison.Ordinal))
                {
                    return assetPath;
                }
            }

            return null;
        }

        internal static bool EvidenceMatches(
            HotReloadNewSourceMembershipEvidence expected,
            HotReloadNewSourceMembershipEvidence current)
        {
            if (!string.Equals(
                    NormalizeProjectRelativePath(expected.ProjectRelativePath),
                    NormalizeProjectRelativePath(current.ProjectRelativePath),
                    PathComparison)
                || !string.Equals(expected.AssemblyName, current.AssemblyName, StringComparison.Ordinal)
                || !string.Equals(expected.TargetDllMvid, current.TargetDllMvid, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeProjectRelativePath(expected.ResolvedAssemblyDefinitionPath),
                    NormalizeProjectRelativePath(current.ResolvedAssemblyDefinitionPath),
                    PathComparison)
                || expected.Boundaries.Length != current.Boundaries.Length)
            {
                return false;
            }

            for (int index = 0; index < expected.Boundaries.Length; index++)
            {
                HotReloadNewSourceMembershipBoundary expectedBoundary = expected.Boundaries[index];
                HotReloadNewSourceMembershipBoundary currentBoundary = current.Boundaries[index];
                if (!string.Equals(
                        NormalizeProjectRelativePath(expectedBoundary.ProjectRelativePath),
                        NormalizeProjectRelativePath(currentBoundary.ProjectRelativePath),
                        PathComparison)
                    || !string.Equals(expectedBoundary.DiskContents, currentBoundary.DiskContents, StringComparison.Ordinal)
                    || !string.Equals(expectedBoundary.ImportedContents, currentBoundary.ImportedContents, StringComparison.Ordinal)
                    || !string.Equals(expectedBoundary.DiskGuid, currentBoundary.DiskGuid, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(expectedBoundary.ImportedGuid, currentBoundary.ImportedGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static UnityCompilationAssembly FindCompilationAssembly(string assemblyName)
        {
            UnityCompilationAssembly[] assemblies = CompilationPipeline.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (assemblies[index].name == assemblyName)
                {
                    return assemblies[index];
                }
            }

            return null;
        }

        private static string ReadMvid(string assemblyPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
            return assemblyDefinition.MainModule.Mvid.ToString();
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
