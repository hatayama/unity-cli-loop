using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Assembly Definition and Assembly Reference Console error detection before compile waits.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleErrorValidationServiceTests
    {
        [Test]
        public void FindErrors_WhenAsmdefErrorMessageExists_ReturnsIssueWithFile()
        {
            // Tests that current Console errors with .asmdef paths are converted into compile issues.
            AssemblyDefinitionConsoleErrorValidationService service = new();
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "Assembly has duplicate references: Unity.InputSystem (Assets/Editor/Sample.asmdef)",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors, Has.Length.EqualTo(1));
            Assert.That(result.Errors[0].File, Is.EqualTo("Assets/Editor/Sample.asmdef"));
            Assert.That(result.Errors[0].Line, Is.EqualTo(0));
            Assert.That(result.Errors[0].Message, Is.EqualTo(entries[0].Message));
        }

        [Test]
        public void FindErrors_WhenAsmrefErrorMessageExists_ReturnsIssueWithFile()
        {
            // Tests that Assembly Reference import failures are detected with the same Console path rule.
            AssemblyDefinitionConsoleErrorValidationService service = new();
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "Assembly Reference has an invalid target (Assets/Tests/Sample.asmref)",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors, Has.Length.EqualTo(1));
            Assert.That(result.Errors[0].File, Is.EqualTo("Assets/Tests/Sample.asmref"));
        }

        [Test]
        public void FindErrors_WhenOnlyNonErrorEntriesMentionAsmdef_ReturnsNoIssues()
        {
            // Tests that visible non-error Console notes do not block compilation.
            AssemblyDefinitionConsoleErrorValidationService service = new();
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Warning,
                    "Package inspected Assets/Editor/Sample.asmdef",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void FindErrors_WhenGenericErrorMentionsAsmdefPath_ReturnsNoIssues()
        {
            // Tests that stale generic Console errors mentioning .asmdef paths do not block compilation.
            AssemblyDefinitionConsoleErrorValidationService service = new();
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "Tool failed while reading Assets/Editor/Sample.asmdef",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Errors, Is.Empty);
        }
    }
}
