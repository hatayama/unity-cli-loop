using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    internal static class ExternalCompilerPathResolver
    {
        public static ExternalCompilerPaths Resolve()
        {
            string editorPath = EditorApplication.applicationPath;
            if (string.IsNullOrEmpty(editorPath))
            {
                return null;
            }

            string contentsPath = ResolveEditorContentsPath(editorPath);
            if (string.IsNullOrEmpty(contentsPath))
            {
                return null;
            }

            string scriptingRootPath = ResolveScriptingRootPath(contentsPath);
            string dotnetHostFileName = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor
                ? "dotnet.exe"
                : "dotnet";

            List<string> missingComponents = new List<string>();
            if (string.IsNullOrEmpty(scriptingRootPath))
            {
                missingComponents.Add(Path.Combine(contentsPath, "NetCoreRuntime"));
                missingComponents.Add(Path.Combine(contentsPath, "DotNetSdkRoslyn"));
                missingComponents.Add(Path.Combine(contentsPath, "Resources", "Scripting", "NetCoreRuntime"));
                missingComponents.Add(Path.Combine(contentsPath, "Resources", "Scripting", "DotNetSdkRoslyn"));
            }

            string dotnetHostPath = Path.Combine(scriptingRootPath ?? contentsPath, "NetCoreRuntime", dotnetHostFileName);
            string compilerDllPath = Path.Combine(scriptingRootPath ?? contentsPath, "DotNetSdkRoslyn", "csc.dll");
            string compilerRuntimeConfigPath = Path.Combine(scriptingRootPath ?? contentsPath, "DotNetSdkRoslyn", "csc.runtimeconfig.json");
            string compilerDepsFilePath = Path.Combine(scriptingRootPath ?? contentsPath, "DotNetSdkRoslyn", "csc.deps.json");
            string codeAnalysisDllPath = Path.Combine(scriptingRootPath ?? contentsPath, "DotNetSdkRoslyn", "Microsoft.CodeAnalysis.dll");
            string codeAnalysisCSharpDllPath = Path.Combine(scriptingRootPath ?? contentsPath, "DotNetSdkRoslyn", "Microsoft.CodeAnalysis.CSharp.dll");
            string netCoreRuntimeSharedRootPath = Path.Combine(scriptingRootPath ?? contentsPath, "NetCoreRuntime", "shared", "Microsoft.NETCore.App");
            string netCoreRuntimeSharedDirectoryPath = ResolveNetCoreRuntimeSharedDirectoryPath(netCoreRuntimeSharedRootPath);

            if (!File.Exists(dotnetHostPath))
            {
                missingComponents.Add(dotnetHostPath);
            }

            if (!File.Exists(compilerDllPath))
            {
                missingComponents.Add(compilerDllPath);
            }

            if (!File.Exists(compilerRuntimeConfigPath))
            {
                missingComponents.Add(compilerRuntimeConfigPath);
            }

            if (!File.Exists(compilerDepsFilePath))
            {
                missingComponents.Add(compilerDepsFilePath);
            }

            if (!File.Exists(codeAnalysisDllPath))
            {
                missingComponents.Add(codeAnalysisDllPath);
            }

            if (!File.Exists(codeAnalysisCSharpDllPath))
            {
                missingComponents.Add(codeAnalysisCSharpDllPath);
            }

            if (string.IsNullOrEmpty(netCoreRuntimeSharedDirectoryPath))
            {
                missingComponents.Add(netCoreRuntimeSharedRootPath);
            }

            if (missingComponents.Count > 0)
            {
                DynamicCompilationHealthMonitor.ReportFastPathUnavailable(
                    editorPath,
                    contentsPath,
                    missingComponents);
                return null;
            }

            return new ExternalCompilerPaths(
                contentsPath,
                scriptingRootPath,
                dotnetHostPath,
                compilerDllPath,
                compilerRuntimeConfigPath,
                compilerDepsFilePath,
                codeAnalysisDllPath,
                codeAnalysisCSharpDllPath,
                netCoreRuntimeSharedDirectoryPath);
        }

        internal static string ResolveScriptingRootPath(string contentsPath)
        {
            if (string.IsNullOrEmpty(contentsPath))
            {
                return null;
            }

            string resourcesScriptingRootPath = Path.Combine(contentsPath, "Resources", "Scripting");
            if (ContainsExternalCompilerLayout(resourcesScriptingRootPath))
            {
                return resourcesScriptingRootPath;
            }

            if (ContainsExternalCompilerLayout(contentsPath))
            {
                return contentsPath;
            }

            return ResolveScriptingRootPathByScan(contentsPath);
        }

        internal static string ResolveNetCoreRuntimeSharedDirectoryPath(string netCoreRuntimeSharedRootPath)
        {
            if (!Directory.Exists(netCoreRuntimeSharedRootPath))
            {
                return null;
            }

            string[] runtimeDirectories = Directory.GetDirectories(netCoreRuntimeSharedRootPath);
            if (runtimeDirectories.Length == 0)
            {
                return null;
            }

            string highestVersionDirectoryPath = runtimeDirectories
                .Select(runtimeDirectoryPath => new
                {
                    Path = runtimeDirectoryPath,
                    VersionText = Path.GetFileName(runtimeDirectoryPath)
                })
                .Where(candidate => Version.TryParse(candidate.VersionText, out _))
                .OrderByDescending(candidate => new Version(candidate.VersionText))
                .ThenByDescending(candidate => candidate.VersionText, StringComparer.Ordinal)
                .Select(candidate => candidate.Path)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(highestVersionDirectoryPath))
            {
                return highestVersionDirectoryPath;
            }

            return runtimeDirectories
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .First();
        }

        private static bool ContainsExternalCompilerLayout(string rootPath)
        {
            return Directory.Exists(Path.Combine(rootPath, "NetCoreRuntime"))
                && Directory.Exists(Path.Combine(rootPath, "DotNetSdkRoslyn"));
        }

        private static string ResolveScriptingRootPathByScan(string contentsPath)
        {
            if (!Directory.Exists(contentsPath))
            {
                return null;
            }

            Queue<(string Path, int Depth)> pendingDirectories = new Queue<(string Path, int Depth)>();
            pendingDirectories.Enqueue((contentsPath, 0));

            while (pendingDirectories.Count > 0)
            {
                (string currentPath, int depth) = pendingDirectories.Dequeue();
                if (ContainsExternalCompilerLayout(currentPath))
                {
                    return currentPath;
                }

                if (depth >= 4)
                {
                    continue;
                }

                foreach (string childDirectoryPath in Directory.GetDirectories(currentPath).OrderBy(path => path, StringComparer.Ordinal))
                {
                    pendingDirectories.Enqueue((childDirectoryPath, depth + 1));
                }
            }

            return null;
        }

        private static string ResolveEditorContentsPath(string editorPath)
        {
            if (editorPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(editorPath, "Contents");
            }

            string editorDirectoryPath = Path.GetDirectoryName(editorPath);
            if (string.IsNullOrEmpty(editorDirectoryPath))
            {
                return null;
            }

            string dataDirectoryPath = Path.Combine(editorDirectoryPath, "Data");
            if (Directory.Exists(dataDirectoryPath))
            {
                return dataDirectoryPath;
            }

            string installRootPath = Path.GetDirectoryName(editorDirectoryPath);
            return string.IsNullOrEmpty(installRootPath)
                ? null
                : installRootPath;
        }
    }
}
