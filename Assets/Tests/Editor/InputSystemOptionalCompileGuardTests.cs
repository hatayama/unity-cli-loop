using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies optional Input System compile guards.
    /// </summary>
    public sealed class InputSystemOptionalCompileGuardTests
    {
        private static readonly string[] StartupPaths =
        {
            "Packages/src/Editor/FirstPartyTools/SimulateMouseInput/SimulateMouseInputEditorStartup.cs",
            "Packages/src/Editor/FirstPartyTools/SimulateKeyboard/SimulateKeyboardEditorStartup.cs",
            "Packages/src/Editor/FirstPartyTools/RecordInput/RecordInputEditorStartup.cs",
            "Packages/src/Editor/FirstPartyTools/ReplayInput/ReplayInputEditorStartup.cs"
        };

        [Test]
        public void InputSystemStartupFiles_WhenScanned_AreGuardedByInputSystemDefine()
        {
            // Tests that Input System startup files are absent from projects without com.unity.inputsystem.
            List<string> violations = new();
            foreach (string startupPath in StartupPaths)
            {
                string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), startupPath);
                string source = File.ReadAllText(absolutePath);
                if (source.TrimStart().StartsWith("#if ULOOP_HAS_INPUT_SYSTEM", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add(startupPath);
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }
    }
}
