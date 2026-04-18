using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using UnityEngine;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Synchronizes skill files when tools are enabled/disabled.
    /// Removes skill directories on disable, re-installs on enable.
    /// </summary>
    public static class ToolSkillSynchronizer
    {
        private const string SkillsDirName = "skills";
        private const string SkillFileName = "SKILL.md";

        public readonly struct SkillTargetDefinition
        {
            public readonly string DirName;
            public readonly string Flag;
            public readonly string DisplayName;

            public SkillTargetDefinition(string dirName, string flag, string displayName)
            {
                DirName = dirName;
                Flag = flag;
                DisplayName = displayName;
            }
        }

        public readonly struct SkillInstallResult
        {
            public readonly int AttemptedTargets;
            public readonly int SucceededTargets;

            public SkillInstallResult(int attemptedTargets, int succeededTargets)
            {
                AttemptedTargets = attemptedTargets;
                SucceededTargets = succeededTargets;
            }

            public int FailedTargets => AttemptedTargets - SucceededTargets;
            public bool IsSuccessful => FailedTargets == 0;
        }

        public readonly struct SkillTargetInfo
        {
            public readonly string DisplayName;
            public readonly string DirName;
            public readonly string InstallFlag;
            public readonly bool HasSkillsDirectory;
            public readonly bool HasExistingSkills;

            public SkillTargetInfo(
                string displayName,
                string dirName,
                string installFlag,
                bool hasSkillsDirectory,
                bool hasExistingSkills)
            {
                DisplayName = displayName;
                DirName = dirName;
                InstallFlag = installFlag;
                HasSkillsDirectory = hasSkillsDirectory;
                HasExistingSkills = hasExistingSkills;
            }
        }

        private static readonly SkillTargetDefinition[] SkillTargets =
        {
            new(".claude", "--claude", "Claude Code"),
            new(".cursor", "--cursor", "Cursor"),
            new(".gemini", "--gemini", "Gemini CLI"),
            new(".codex", "--codex", "Codex CLI"),
            new(".agents", "--agents", "Other (.agents)"),
            new(".agent", "--antigravity", "Antigravity")
        };

        internal static readonly string[] SkillTargetDirs = SkillTargets.Select(t => t.DirName).ToArray();

        public static void RemoveSkillFiles(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();

            foreach (string targetDir in SkillTargetDirs)
            {
                string skillsRoot = Path.Combine(projectRoot, targetDir, SkillsDirName);
                if (!Directory.Exists(skillsRoot))
                {
                    continue;
                }

                string[] skillDirs = Directory.GetDirectories(skillsRoot, CliConstants.SKILL_DIR_GLOB);
                foreach (string skillDir in skillDirs)
                {
                    if (SkillMatchesTool(skillDir, toolName))
                    {
                        Directory.Delete(skillDir, true);
                    }
                }
            }
        }

        public static bool IsSkillInstalled(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();

            foreach (string targetDir in SkillTargetDirs)
            {
                string skillsRoot = Path.Combine(projectRoot, targetDir, SkillsDirName);
                if (!Directory.Exists(skillsRoot))
                {
                    continue;
                }

                string[] skillDirs = Directory.GetDirectories(skillsRoot, CliConstants.SKILL_DIR_GLOB);
                foreach (string skillDir in skillDirs)
                {
                    if (SkillMatchesTool(skillDir, toolName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static List<SkillTargetInfo> DetectTargets()
        {
            return DetectTargets(requireSkillsDirectory: false);
        }

        internal static List<SkillTargetInfo> DetectTargets(bool requireSkillsDirectory)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return DetectTargets(projectRoot, requireSkillsDirectory);
        }

        internal static List<SkillTargetInfo> DetectTargets(string projectRoot, bool requireSkillsDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<SkillTargetInfo> targets = new();

            foreach (SkillTargetDefinition target in SkillTargets)
            {
                string targetRoot = Path.Combine(projectRoot, target.DirName);
                if (!Directory.Exists(targetRoot))
                {
                    continue;
                }

                string skillsRoot = Path.Combine(targetRoot, SkillsDirName);
                bool hasSkillsDirectory = Directory.Exists(skillsRoot);
                if (requireSkillsDirectory && !hasSkillsDirectory)
                {
                    continue;
                }

                bool hasULoopSkills = hasSkillsDirectory
                    && Directory.EnumerateDirectories(skillsRoot, CliConstants.SKILL_DIR_GLOB)
                        .Any(skillDir => File.Exists(Path.Combine(skillDir, SkillFileName)));
                targets.Add(new SkillTargetInfo(
                    target.DisplayName,
                    target.DirName,
                    target.Flag,
                    hasSkillsDirectory,
                    hasULoopSkills));
            }

            return targets;
        }

        /// <summary>
        /// Re-installs skills only for targets that already opted in via an existing skills directory.
        /// </summary>
        public static async Task<SkillInstallResult> InstallSkillFiles()
        {
            List<SkillTargetInfo> targets = DetectTargets(requireSkillsDirectory: true);
            return await InstallSkillFiles(targets);
        }

        public static async Task<SkillInstallResult> InstallSkillFiles(List<SkillTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");

            string uloopPath = NodeEnvironmentResolver.FindExecutablePath(CliConstants.EXECUTABLE_NAME);
            string uloopFileName = uloopPath ?? CliConstants.EXECUTABLE_NAME;
            string nodePath = NodeEnvironmentResolver.FindNodePath();

            int succeeded = 0;

            foreach (SkillTargetInfo target in targets)
            {
                bool success = await RunSkillsInstall(target.InstallFlag, uloopFileName, nodePath);
                if (success)
                {
                    succeeded++;
                }
            }

            return new SkillInstallResult(targets.Count, succeeded);
        }

        private static bool SkillMatchesTool(string skillDir, string toolName)
        {
            string skillMdPath = Path.Combine(skillDir, SkillFileName);
            if (File.Exists(skillMdPath))
            {
                string content = File.ReadAllText(skillMdPath);
                string parsed = ParseToolNameFromFrontmatter(content);
                if (!string.IsNullOrEmpty(parsed))
                {
                    return parsed == toolName;
                }
            }
            // Fallback: directory name "uloop-{toolName}"
            string dirName = Path.GetFileName(skillDir);
            return dirName == $"{CliConstants.SKILL_DIR_PREFIX}{toolName}";
        }

        private static string ParseToolNameFromFrontmatter(string content)
        {
            Match frontmatterMatch = Regex.Match(content, @"^---\r?\n([\s\S]*?)\r?\n---");
            if (!frontmatterMatch.Success)
            {
                return null;
            }

            string frontmatter = frontmatterMatch.Groups[1].Value;
            Match toolNameMatch = Regex.Match(frontmatter, @"^toolName:\s*(.+)$", RegexOptions.Multiline);
            if (!toolNameMatch.Success)
            {
                return null;
            }

            return toolNameMatch.Groups[1].Value.Trim();
        }

        private static async Task<bool> RunSkillsInstall(string targetFlag, string uloopFileName, string nodePath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = uloopFileName,
                Arguments = $"skills install {targetFlag}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            NodeEnvironmentResolver.SetupEnvironmentPath(startInfo, nodePath);

            return await Task.Run(() =>
            {
                Process process = ProcessStartHelper.TryStart(startInfo);
                if (process == null)
                {
                    Debug.LogWarning($"[uLoopMCP] Failed to start uloop process for: skills install {targetFlag}");
                    return false;
                }

                using (process)
                {
                    // Read stderr asynchronously to prevent buffer deadlock
                    // when stdout and stderr are both redirected
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = stderrTask.Result;
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning($"[uLoopMCP] uloop skills install {targetFlag} exited with code {process.ExitCode}: {stderr}");
                        return false;
                    }

                    return true;
                }
            });
        }
    }
}
