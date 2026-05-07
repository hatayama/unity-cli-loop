using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    // Guards the facade/service split introduced by the onion-architecture refactor.
    /// <summary>
    /// Test fixture that verifies Static Facade State Guard behavior.
    /// </summary>
    public sealed class StaticFacadeStateGuardTests
    {
        private static readonly string[] MigratedFacadePaths = new string[]
        {
            "Packages/src/Editor/Application/CLI/CliSetupApplicationService.cs",
            "Packages/src/Editor/Application/Config/ToolSettings.cs",
            "Packages/src/Editor/Application/Config/ULoopSettings.cs",
            "Packages/src/Editor/Application/Config/UnityCliLoopEditorSettings.cs",
            "Packages/src/Editor/Application/Api/Tools/Core/UnityCliLoopToolRegistrar.cs",
            "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/DynamicCodeServices.cs",
            "Packages/src/Editor/FirstPartyTools/ExecuteDynamicCode/Execution/DynamicCodeForegroundWarmupState.cs",
            "Packages/src/Editor/ToolContracts/EditorDelayManager.cs",
            "Packages/src/Editor/FirstPartyTools/Common/InputRecording/InputRecorder.cs",
            "Packages/src/Editor/FirstPartyTools/RecordInput/Application/RecordingsApplicationFacade.cs",
            "Packages/src/Editor/FirstPartyTools/Common/InputRecording/InputReplayer.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateKeyboard/Application/KeyboardKeyState.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateMouseInput/Application/MouseInputState.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateMouseUi/Application/MouseDragState.cs",
            "Packages/src/Editor/FirstPartyTools/Common/Overlay/OverlayCanvasFactory.cs",
            "Packages/src/Editor/Application/Core/CoreTools/Util/MainThreadSwitcher.cs",
            "Packages/src/Editor/ToolContracts/VibeLogger.cs",
            "Packages/src/Editor/Application/Server/UnityCliLoopServerApplicationService.cs",
            "Packages/src/Runtime/RecordInput/RecordInputOverlayState.cs",
            "Packages/src/Runtime/ReplayInput/ReplayInputOverlayState.cs",
            "Packages/src/Runtime/SimulateKeyboard/SimulateKeyboardOverlayState.cs",
            "Packages/src/Runtime/SimulateMouseInput/SimulateMouseInputOverlayState.cs",
            "Packages/src/Runtime/SimulateMouseUi/SimulateMouseUiOverlayState.cs"
        };

        private static readonly string[] PublicContractPaths = new string[]
        {
            "Packages/src/Editor/ToolContracts/UnityCliLoopToolResponse.cs"
        };

        private static readonly string[] InstanceServicePaths = new string[]
        {
            "Packages/src/Editor/Application/Core/ApplicationServices/SessionRecoveryService.cs"
        };

        private static readonly Regex DirectMutableStaticFieldPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+(?!readonly\b)(?!event\b)(?!extern\b)[^(\r\n;=]*[;=]",
            RegexOptions.Compiled);

        private static readonly Regex ReadonlyMutableStaticFieldPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+readonly\s+([^;=]+)",
            RegexOptions.Compiled);

        private static readonly Regex DirectStaticEventPattern = new Regex(
            @"\b(private|internal|public|protected)\s+static\s+event\b",
            RegexOptions.Compiled);

        private static readonly Regex StaticClassPattern = new Regex(
            @"\b(public|internal|private|protected)\s+static\s+class\b",
            RegexOptions.Compiled);

        [Test]
        public void MigratedFacadeFiles_WhenScanned_DoNotOwnMutableStaticState()
        {
            // Tests that migrated facades keep state inside instance services instead of direct static fields.
            List<string> violations = FindMutableStaticFieldViolations();

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void ProductionSources_WhenScanned_DoNotDeclareStaticEvents()
        {
            // Tests that static entrypoints do not hide event subscription lifetimes.
            List<string> violations = FindDirectStaticEventViolations();

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void PublicContracts_WhenScanned_DoNotOwnMutableStaticState()
        {
            // Tests that extension-facing contracts do not share mutable state across tool responses.
            List<string> violations = FindMutableStaticFieldViolations(PublicContractPaths);

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void InstanceServices_WhenScanned_AreNotStaticClasses()
        {
            // Tests that migrated services stay instance-owned instead of sliding back into static services.
            List<string> violations = FindStaticClassViolations(InstanceServicePaths);

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void MutableStaticFieldLine_WhenInitializerUsesTargetTypedNew_IsReported()
        {
            // Tests that the guard catches mutable static fields even when the initializer contains new().
            string line = "private static List<int> Cache = new();";
            bool isViolation =
                !IsAllowedStaticLine(line)
                && (DirectMutableStaticFieldPattern.IsMatch(line)
                    || ReadonlyMutableStaticFieldPattern.IsMatch(line));

            Assert.That(isViolation, Is.True);
        }

        private static List<string> FindMutableStaticFieldViolations()
        {
            return FindMutableStaticFieldViolations(MigratedFacadePaths);
        }

        private static List<string> FindMutableStaticFieldViolations(string[] relativePaths)
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();

            for (int pathIndex = 0; pathIndex < relativePaths.Length; pathIndex++)
            {
                string relativePath = relativePaths[pathIndex];
                string absolutePath = Path.Combine(projectRoot, relativePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (IsAllowedStaticLine(line))
                    {
                        continue;
                    }

                    if (DirectMutableStaticFieldPattern.IsMatch(line)
                        || ReadonlyMutableStaticFieldPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static List<string> FindDirectStaticEventViolations()
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string packagesSrcRoot = Path.Combine(projectRoot, "Packages/src");
            string[] sourcePaths = Directory.GetFiles(packagesSrcRoot, "*.cs", SearchOption.AllDirectories);

            for (int pathIndex = 0; pathIndex < sourcePaths.Length; pathIndex++)
            {
                string absolutePath = sourcePaths[pathIndex];
                string relativePath = Path.GetRelativePath(projectRoot, absolutePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (DirectStaticEventPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static List<string> FindStaticClassViolations(string[] relativePaths)
        {
            List<string> violations = new();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();

            for (int pathIndex = 0; pathIndex < relativePaths.Length; pathIndex++)
            {
                string relativePath = relativePaths[pathIndex];
                string absolutePath = Path.Combine(projectRoot, relativePath);
                string[] lines = File.ReadAllLines(absolutePath);

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (StaticClassPattern.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineIndex + 1}: {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        private static bool IsAllowedStaticLine(string line)
        {
            if (line.Contains("=>"))
            {
                return true;
            }

            if (line.Contains("(") && !line.Contains("=") && !line.TrimEnd().EndsWith(";"))
            {
                return true;
            }

            return line.Contains("ServiceValue")
                   || line.Contains("RepositoryValue")
                   || line.Contains("RegistryValue");
        }
    }
}
