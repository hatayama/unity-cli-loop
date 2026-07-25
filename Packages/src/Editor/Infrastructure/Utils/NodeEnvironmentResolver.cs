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

        private const string WHICH_START_MARKER = "__WHICH_START__";
        private const string WHICH_END_MARKER = "__WHICH_END__";
        private const string POSIX_FALLBACK_SHELL_PATH = "/bin/sh";
        private const string DIRECTORY_SERVICE_EXECUTABLE_PATH = "/usr/bin/dscl";
        private const string DIRECTORY_SERVICE_USERS_PATH_PREFIX = "/Users/";
        private const string DIRECTORY_SERVICE_USER_SHELL_ATTRIBUTE = "UserShell";
        private const string DIRECTORY_SERVICE_USER_SHELL_PREFIX = DIRECTORY_SERVICE_USER_SHELL_ATTRIBUTE + ":";

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

        internal static string GetUserShell()
        {
            string environmentShell = System.Environment.GetEnvironmentVariable("SHELL");
            if (IsExistingShell(environmentShell, File.Exists))
            {
                return environmentShell;
            }

            string directoryServiceShell = TryReadDirectoryServiceUserShell(System.Environment.UserName);
            return SelectUserShell(null, directoryServiceShell, File.Exists);
        }

        internal static string SelectUserShell(
            string environmentShell,
            string directoryServiceShell,
            System.Func<string, bool> fileExists)
        {
            UnityEngine.Debug.Assert(fileExists != null, "fileExists must not be null");

            if (IsExistingShell(environmentShell, fileExists))
            {
                return environmentShell;
            }

            if (IsExistingShell(directoryServiceShell, fileExists))
            {
                return directoryServiceShell;
            }

            return POSIX_FALLBACK_SHELL_PATH;
        }

        internal static string ExtractDirectoryServiceUserShell(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            string[] lines = output.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith(DIRECTORY_SERVICE_USER_SHELL_PREFIX, System.StringComparison.Ordinal))
                {
                    continue;
                }

                string shell = line.Substring(DIRECTORY_SERVICE_USER_SHELL_PREFIX.Length).Trim();
                return string.IsNullOrEmpty(shell) ? null : shell;
            }

            return null;
        }

        private static bool IsExistingShell(
            string shell,
            System.Func<string, bool> fileExists)
        {
            return !string.IsNullOrEmpty(shell) && fileExists(shell);
        }

        private static string TryReadDirectoryServiceUserShell(string userName)
        {
            if (!File.Exists(DIRECTORY_SERVICE_EXECUTABLE_PATH) || !IsSafeDirectoryServiceUserName(userName))
            {
                return null;
            }

            ProcessStartInfo startInfo = new()            {
                FileName = DIRECTORY_SERVICE_EXECUTABLE_PATH,
                Arguments = ". -read " + DIRECTORY_SERVICE_USERS_PATH_PREFIX + userName
                    + " " + DIRECTORY_SERVICE_USER_SHELL_ATTRIBUTE,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            return ExtractDirectoryServiceUserShell(ExecuteAndGetOutput(startInfo));
        }

        private static bool IsSafeDirectoryServiceUserName(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                return false;
            }

            foreach (char character in userName)
            {
                if (char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsWindowsEditor(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor;
        }
    }
}
