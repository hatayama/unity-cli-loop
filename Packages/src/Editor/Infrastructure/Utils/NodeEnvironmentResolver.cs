using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Utility class for detecting executable paths through the user's shell environment.
    /// Uses login shell to resolve PATH, matching the user's terminal environment.
    /// </summary>
    public static class NodeEnvironmentResolver
    {
        private const int PROCESS_TIMEOUT_MS = 5000;

        /// <summary>
        /// Finds an executable path using platform-appropriate resolution.
        /// On Windows, resolves .cmd shims via 'where' command.
        /// On Unix, resolves via login shell 'which' command.
        /// </summary>
        public static string FindExecutablePath(string executableName)
        {
            return FindExecutablePathAtPlatform(executableName, UnityEngine.Application.platform);
        }

        internal static string FindExecutablePathAtPlatform(string executableName, RuntimePlatform platform)
        {
            if (IsWindowsEditor(platform))
            {
                return FindExecutableWindows(executableName);
            }

            return FindExecutableUnix(executableName);
        }

        private static string FindExecutableUnix(string executableName)
        {
            return TryWhichCommand(executableName);
        }

        private static string FindExecutableWindows(string executableName)
        {
            return TryWhereCommand(executableName);
        }

        // Only returns the login shell's which result - no hardcoded fallback paths.
        // Scanning version-manager directories directly caused false positives (e.g. detecting
        // an uninstalled CLI version), which was the original bug this PR fixes.
        // Interactive login shell (-l -i) loads .zprofile and .zshrc/.bashrc, matching the user's terminal
        // Markers isolate which output from shell startup banners; ExtractAbsolutePathLine filters alias text
        // executableName is not shell-escaped because all callers pass hardcoded constants (YAGNI)
        private static string TryWhichCommand(string executableName)
        {
            string shell = GetUserShell();
            ProcessStartInfo startInfo = new()            {
                FileName = shell,
                Arguments = "-l -i -c \"echo " + WHICH_START_MARKER + "; which " + executableName + "; echo " + WHICH_END_MARKER + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output = ExecuteAndGetOutput(startInfo);
            string block = ExtractBetweenMarkers(output, WHICH_START_MARKER, WHICH_END_MARKER);
            return ExtractAbsolutePathLine(block);
        }

        /// <summary>
        /// Finds the first executable path for the given name using the Windows 'where' command.
        /// Prioritizes .cmd/.exe over extensionless entries because native Windows shims must be launched directly.
        /// </summary>
        private static string TryWhereCommand(string executableName)
        {
            string[] paths = TryWhereCommandAll(executableName);
            if (paths == null || paths.Length == 0)
            {
                return null;
            }

            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path);
                if (string.Equals(extension, ".cmd", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".exe", System.StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return paths[0];
        }

        private static string[] TryWhereCommandAll(string executableName)
        {
            ProcessStartInfo startInfo = new()            {
                FileName = "cmd.exe",
                Arguments = $"/c where {executableName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output = ExecuteAndGetOutput(startInfo);
            if (!string.IsNullOrEmpty(output))
            {
                string[] lines = output.Split('\n');
                List<string> result = new();
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        result.Add(trimmed);
                    }
                }
                return result.Count > 0 ? result.ToArray() : null;
            }

            return null;
        }

        private static string ExecuteAndGetOutput(ProcessStartInfo startInfo)
        {
            UnityEngine.Debug.Assert(startInfo != null, "startInfo must not be null");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardOutput, "RedirectStandardOutput must be true");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardError, "RedirectStandardError must be true");

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return null;
                }

                System.Text.StringBuilder stdoutBuilder = new();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stdoutBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) => { };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(PROCESS_TIMEOUT_MS))
                {
                    // Process may exit between WaitForExit(timeout) and Kill() (TOCTOU race)
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch (System.InvalidOperationException)
                    {
                        UnityEngine.Debug.Log("Process already exited before Kill() was called");
                    }

                    return null;
                }

                // Parameterless WaitForExit flushes async output buffers
                process.WaitForExit();

                string output = stdoutBuilder.ToString().Trim();
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    return output;
                }
            }

            return null;
        }

        private const string PATH_START_MARKER = "__PATH_START__";
        private const string PATH_END_MARKER = "__PATH_END__";
        private const string WHICH_START_MARKER = "__WHICH_START__";
        private const string WHICH_END_MARKER = "__WHICH_END__";

        // Uses markers to extract PATH value, ignoring any banner/echo output from shell startup files
        internal static string GetLoginShellPathAtPlatform(RuntimePlatform platform)
        {
            if (IsWindowsEditor(platform))
            {
                return null;
            }

            string shell = GetUserShell();
            ProcessStartInfo startInfo = new()            {
                FileName = shell,
                Arguments = "-l -i -c \"echo " + PATH_START_MARKER + "; printenv PATH; echo " + PATH_END_MARKER + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output = ExecuteAndGetOutput(startInfo);
            return ExtractBetweenMarkers(output, PATH_START_MARKER, PATH_END_MARKER);
        }

        internal static string ExtractBetweenMarkers(string output, string startMarker, string endMarker)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            int startIndex = output.IndexOf(startMarker, System.StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return null;
            }

            int searchFrom = startIndex + startMarker.Length;
            int endIndex = output.IndexOf(endMarker, searchFrom, System.StringComparison.Ordinal);
            if (endIndex < 0)
            {
                return null;
            }

            return output.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
        }

        internal static string ExtractAbsolutePathLine(string block)
        {
            if (string.IsNullOrEmpty(block))
            {
                return null;
            }

            string[] lines = block.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!string.IsNullOrEmpty(line) && Path.IsPathRooted(line))
                {
                    return line;
                }
            }

            return null;
        }

        // Falls back to /bin/sh when $SHELL is unset or invalid. /bin/sh won't load
        // .zshrc/.bashrc, so version-manager paths may be missed — but $SHELL being unset
        // is extremely rare on macOS/Linux and there is no reliable way to detect the user's
        // preferred shell without it.
        private static string GetUserShell()
        {
            string shell = System.Environment.GetEnvironmentVariable("SHELL");
            // $SHELL is external input — validate the path exists to avoid Process.Start exceptions
            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            {
                return shell;
            }

            return "/bin/sh";
        }

        private static bool IsWindowsEditor(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor;
        }
    }
}
