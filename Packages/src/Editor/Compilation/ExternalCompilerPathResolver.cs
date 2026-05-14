using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    internal static class ExternalCompilerPathResolver
    {
        private const string NetCoreRuntimeDirectoryName = "NetCoreRuntime";
        private const string DotNetSdkRoslynDirectoryName = "DotNetSdkRoslyn";
        private const string DotNetSdkDirectoryName = "DotNetSdk";
        private const string DotNetSdkSdkDirectoryName = "sdk";
        private const string RoslynDirectoryName = "Roslyn";
        private const string CompilerBincoreDirectoryName = "bincore";
        private const string CompilerDllFileName = "csc.dll";
        private const string CompilerRuntimeConfigFileName = "csc.runtimeconfig.json";
        private const string CompilerDepsFileName = "csc.deps.json";
        private const string CodeAnalysisDllFileName = "Microsoft.CodeAnalysis.dll";
        private const string CodeAnalysisCSharpDllFileName = "Microsoft.CodeAnalysis.CSharp.dll";
        private const string NetCoreRuntimeSharedDirectoryName = "shared";
        private const string NetCoreRuntimeSharedFrameworkName = "Microsoft.NETCore.App";

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
            string effectiveScriptingRootPath = scriptingRootPath ?? contentsPath;
            string compilerDirectoryPath = ResolveCompilerDirectoryPath(effectiveScriptingRootPath);

            List<string> missingComponents = new List<string>();
            if (string.IsNullOrEmpty(scriptingRootPath))
            {
                missingComponents.Add(Path.Combine(contentsPath, NetCoreRuntimeDirectoryName));
                missingComponents.Add(Path.Combine(contentsPath, DotNetSdkRoslynDirectoryName));
                missingComponents.Add(Path.Combine(contentsPath, DotNetSdkDirectoryName, DotNetSdkSdkDirectoryName, "*", RoslynDirectoryName, CompilerBincoreDirectoryName));
                missingComponents.Add(Path.Combine(contentsPath, "Resources", "Scripting", NetCoreRuntimeDirectoryName));
                missingComponents.Add(Path.Combine(contentsPath, "Resources", "Scripting", DotNetSdkRoslynDirectoryName));
                missingComponents.Add(Path.Combine(contentsPath, "Resources", "Scripting", DotNetSdkDirectoryName, DotNetSdkSdkDirectoryName, "*", RoslynDirectoryName, CompilerBincoreDirectoryName));
            }

            if (string.IsNullOrEmpty(compilerDirectoryPath))
            {
                compilerDirectoryPath = Path.Combine(effectiveScriptingRootPath, DotNetSdkRoslynDirectoryName);
                missingComponents.Add(compilerDirectoryPath);
                missingComponents.Add(Path.Combine(effectiveScriptingRootPath, DotNetSdkDirectoryName, DotNetSdkSdkDirectoryName, "*", RoslynDirectoryName, CompilerBincoreDirectoryName));
            }

            string dotnetHostPath = Path.Combine(effectiveScriptingRootPath, NetCoreRuntimeDirectoryName, dotnetHostFileName);
            string compilerDllPath = Path.Combine(compilerDirectoryPath, CompilerDllFileName);
            string compilerRuntimeConfigPath = Path.Combine(compilerDirectoryPath, CompilerRuntimeConfigFileName);
            string compilerDepsFilePath = Path.Combine(compilerDirectoryPath, CompilerDepsFileName);
            string codeAnalysisDllPath = Path.Combine(compilerDirectoryPath, CodeAnalysisDllFileName);
            string codeAnalysisCSharpDllPath = Path.Combine(compilerDirectoryPath, CodeAnalysisCSharpDllFileName);
            string netCoreRuntimeSharedRootPath = Path.Combine(effectiveScriptingRootPath, NetCoreRuntimeDirectoryName, NetCoreRuntimeSharedDirectoryName, NetCoreRuntimeSharedFrameworkName);
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

        internal static string ResolveCompilerDirectoryPath(string scriptingRootPath)
        {
            if (string.IsNullOrEmpty(scriptingRootPath))
            {
                return null;
            }

            string legacyCompilerDirectoryPath = Path.Combine(scriptingRootPath, DotNetSdkRoslynDirectoryName);
            if (Directory.Exists(legacyCompilerDirectoryPath))
            {
                return legacyCompilerDirectoryPath;
            }

            return ResolveDotNetSdkCompilerDirectoryPath(scriptingRootPath);
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
            return Directory.Exists(Path.Combine(rootPath, NetCoreRuntimeDirectoryName))
                && !string.IsNullOrEmpty(ResolveCompilerDirectoryPath(rootPath));
        }

        private static string ResolveDotNetSdkCompilerDirectoryPath(string scriptingRootPath)
        {
            string sdkRootPath = Path.Combine(scriptingRootPath, DotNetSdkDirectoryName, DotNetSdkSdkDirectoryName);
            if (!Directory.Exists(sdkRootPath))
            {
                return null;
            }

            List<string> sdkDirectoryPaths = Directory.GetDirectories(sdkRootPath).ToList();
            sdkDirectoryPaths.Sort(CompareSdkDirectoryPathsDescending);

            foreach (string sdkDirectoryPath in sdkDirectoryPaths)
            {
                string compilerDirectoryPath = Path.Combine(sdkDirectoryPath, RoslynDirectoryName, CompilerBincoreDirectoryName);
                if (Directory.Exists(compilerDirectoryPath))
                {
                    return compilerDirectoryPath;
                }
            }

            return null;
        }

        private static int CompareSdkDirectoryPathsDescending(string leftPath, string rightPath)
        {
            string leftVersionText = Path.GetFileName(leftPath);
            string rightVersionText = Path.GetFileName(rightPath);
            bool leftIsVersion = Version.TryParse(leftVersionText, out Version leftVersion);
            bool rightIsVersion = Version.TryParse(rightVersionText, out Version rightVersion);

            if (leftIsVersion && rightIsVersion)
            {
                int versionComparison = rightVersion.CompareTo(leftVersion);
                if (versionComparison != 0)
                {
                    return versionComparison;
                }
            }
            else if (leftIsVersion)
            {
                return -1;
            }
            else if (rightIsVersion)
            {
                return 1;
            }

            return string.Compare(rightVersionText, leftVersionText, StringComparison.Ordinal);
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
