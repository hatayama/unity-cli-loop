using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
            AssemblyDefinitionConsoleErrorValidationService service = new(
                (assetPath, message) => true);
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
            AssemblyDefinitionConsoleErrorValidationService service = new(
                (assetPath, message) => true);
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
        public void FindErrors_WhenImporterErrorIsNotCurrent_ReturnsNoIssues()
        {
            // Tests that retained importer Console logs do not block compile after the asset state is valid.
            AssemblyDefinitionConsoleErrorValidationService service = new(
                (assetPath, message) => false);
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "Assembly has duplicate references: Unity.InputSystem (Assets/Editor/Sample.asmdef)",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void FindErrors_WhenGenericAsmrefImportErrorIsCurrent_ReturnsIssueWithFile()
        {
            // Tests that asmref import errors are detected even when Unity uses generic parse wording.
            AssemblyDefinitionConsoleErrorValidationService service = new(
                (assetPath, message) => true);
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "JSON parse error in Assets/Tests/Sample.asmref",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors, Has.Length.EqualTo(1));
            Assert.That(result.Errors[0].File, Is.EqualTo("Assets/Tests/Sample.asmref"));
        }

        [Test]
        public void FindErrors_WhenAssetPathContainsParentheses_ReturnsIssueWithFile()
        {
            // Tests that Unity asset paths with parentheses are still parsed from Console errors.
            AssemblyDefinitionConsoleErrorValidationService service = new(
                (assetPath, message) => true);
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    "JSON parse error: Missing a name for object member. (Assets/Foo (Editor)/Bad.asmref)",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.Errors, Has.Length.EqualTo(1));
            Assert.That(result.Errors[0].File, Is.EqualTo("Assets/Foo (Editor)/Bad.asmref"));
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

        [Test]
        public void FindErrors_WhenValidRegistryPackageAsmdefPathIsMentioned_ReturnsNoIssues()
        {
            // Tests that Package Manager virtual asset paths resolve before checking current file contents.
            const string packageAsmdefPath = "Packages/com.unity.cinemachine/Runtime/com.unity.cinemachine.asmdef";
            Assert.That(AssetImporter.GetAtPath(packageAsmdefPath), Is.Not.Null);
            AssemblyDefinitionConsoleErrorValidationService service = new();
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(
                    UnityCliLoopLogType.Error,
                    $"JSON parse error in {packageAsmdefPath}",
                    "")
            };

            AssemblyDefinitionConsoleErrorResult result = service.FindErrors(entries);

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Errors, Is.Empty);
        }

        [Test]
        public void CreateFailureMessage_WhenErrorsHaveFiles_ListsEachIssueWithItsFile()
        {
            // Pins the failure message shape consumed by CompileResult.Message for compile responses.
            AssemblyDefinitionConsoleError[] errors =
            {
                new("duplicate references", "Assets/Editor/Sample.asmdef", 0),
                new("invalid target", "Assets/Tests/Sample.asmref", 0)
            };

            string message = AssemblyDefinitionConsoleErrorValidationService.CreateFailureMessage(errors);

            Assert.That(message, Does.StartWith(UnityCliLoopConstants.ERROR_MESSAGE_ASSEMBLY_DEFINITION_IMPORT_ERROR));
            Assert.That(message, Does.Contain("- Assets/Editor/Sample.asmdef: duplicate references"));
            Assert.That(message, Does.Contain("- Assets/Tests/Sample.asmref: invalid target"));
        }

        [Test]
        public void CreateFailureMessage_WhenErrorHasNoFile_OmitsFilePrefix()
        {
            // Pins fallback formatting for issues without a resolvable asset path.
            AssemblyDefinitionConsoleError[] errors = { new("generic import failure", "", 0) };

            string message = AssemblyDefinitionConsoleErrorValidationService.CreateFailureMessage(errors);

            Assert.That(message, Does.Contain("- generic import failure"));
            Assert.That(message, Does.Not.Contain(": generic import failure"));
        }

        [Test]
        public void CreateFailureMessage_WhenMoreThanTenErrorsExist_ListsOnlyFirstTen()
        {
            // Pins the display cap so console failure messages stay readable with many issues.
            AssemblyDefinitionConsoleError[] errors = new AssemblyDefinitionConsoleError[12];
            for (int i = 0; i < errors.Length; i++)
            {
                errors[i] = new AssemblyDefinitionConsoleError($"issue-{i}", $"Assets/Sample{i}.asmdef", 0);
            }

            string message = AssemblyDefinitionConsoleErrorValidationService.CreateFailureMessage(errors);

            Assert.That(message, Does.Contain("issue-9"));
            Assert.That(message, Does.Not.Contain("issue-10"));
            Assert.That(message, Does.Not.Contain("issue-11"));
        }

        [Test]
        public void AssemblyDefinitionConsoleErrorResult_WhenErrorsExist_ExposesFormattedMessage()
        {
            // Pins that the result DTO's Message mirrors CreateFailureMessage for compile failure reporting.
            AssemblyDefinitionConsoleError[] errors = { new("duplicate references", "Assets/Editor/Sample.asmdef", 0) };

            AssemblyDefinitionConsoleErrorResult result = new(errors);

            Assert.That(result.Message, Is.EqualTo(AssemblyDefinitionConsoleErrorValidationService.CreateFailureMessage(errors)));
        }

        [Test]
        public void AssemblyDefinitionConsoleErrorResult_WhenNoErrorsExist_HasNullMessage()
        {
            // Pins that an empty result never triggers Message-based failure handling.
            AssemblyDefinitionConsoleErrorResult result = new(System.Array.Empty<AssemblyDefinitionConsoleError>());

            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.Message, Is.Null);
        }
    }
}
