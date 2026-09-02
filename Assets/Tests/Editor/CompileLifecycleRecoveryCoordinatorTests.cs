using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins the recovery decisions made by CompileLifecycleRecoveryCoordinator when the watchdog
    /// reports a start timeout or a missed finish callback, without running real Unity compilation.
    /// </summary>
    [TestFixture]
    public sealed class CompileLifecycleRecoveryCoordinatorTests
    {
        [Test]
        public void HandleCompileStartTimeout_WhenAssemblyDefinitionErrorsExist_AbortsWithAssemblyDefinitionResult()
        {
            // Verifies assembly definition errors take priority over the generic start-timeout abort message.
            const string asmdefPath = "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef";
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors = new(
                new AssemblyDefinitionConsoleError[]
                {
                    new("Assembly has duplicate references", asmdefPath, 0)
                });
            CompileResult abortedWithResult = null;
            string abortedWithMessage = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: _ => assemblyDefinitionErrors,
                validateNoDuplicateAsmdefNames: ValidationResult.Success,
                abortWithResult: result => abortedWithResult = result,
                abort: message => abortedWithMessage = message);

            coordinator.HandleCompileStartTimeout(1234);

            Assert.That(abortedWithResult, Is.Not.Null);
            Assert.That(abortedWithResult.Success, Is.False);
            Assert.That(abortedWithResult.ErrorCount, Is.EqualTo(1));
            Assert.That(abortedWithResult.Errors[0].file, Is.EqualTo(asmdefPath));
            Assert.That(abortedWithMessage, Is.Null);
        }

        [Test]
        public void HandleCompileStartTimeout_WhenDuplicateAsmdefNamesExist_AbortsWithValidationMessage()
        {
            // Verifies duplicate asmdef name validation is checked once assembly definition errors are ruled out.
            const string duplicateAsmdefMessage = "Duplicate assembly definition name: Sample";
            CompileResult abortedWithResult = null;
            string abortedWithMessage = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: _ => new AssemblyDefinitionConsoleErrorResult(
                    new AssemblyDefinitionConsoleError[0]),
                validateNoDuplicateAsmdefNames: () => ValidationResult.Failure(duplicateAsmdefMessage),
                abortWithResult: result => abortedWithResult = result,
                abort: message => abortedWithMessage = message);

            coordinator.HandleCompileStartTimeout(1234);

            Assert.That(abortedWithResult, Is.Null);
            Assert.That(abortedWithMessage, Is.EqualTo(duplicateAsmdefMessage));
        }

        [Test]
        public void HandleCompileStartTimeout_WhenNoKnownCauseExists_AbortsWithGenericStartTimeoutMessage()
        {
            // Verifies the fallback message is unchanged when neither known recovery cause applies.
            CompileResult abortedWithResult = null;
            string abortedWithMessage = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: _ => new AssemblyDefinitionConsoleErrorResult(
                    new AssemblyDefinitionConsoleError[0]),
                validateNoDuplicateAsmdefNames: ValidationResult.Success,
                abortWithResult: result => abortedWithResult = result,
                abort: message => abortedWithMessage = message);

            coordinator.HandleCompileStartTimeout(1234);

            Assert.That(abortedWithResult, Is.Null);
            Assert.That(
                abortedWithMessage,
                Is.EqualTo(
                    "Compilation did not start. Possible causes: editor update/reload locks, Auto Refresh disabled, or no script changes."));
        }

        [Test]
        public void HandleCompileStoppedWithoutFinishEvent_WhenAssemblyDefinitionErrorsExist_AbortsWithAssemblyDefinitionResult()
        {
            // Verifies missed-callback recovery reports actionable asmdef errors instead of an indeterminate result.
            const string asmdefPath = "Assets/Tests/EditMode/Sample.EditMode.Tests.asmdef";
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors = new(
                new AssemblyDefinitionConsoleError[]
                {
                    new("Assembly has duplicate references", asmdefPath, 0)
                });
            CompileResult abortedWithResult = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: _ => assemblyDefinitionErrors,
                getCompileMessages: () => new CompilerMessage[0],
                getIsForceCompile: () => false,
                abortWithResult: result => abortedWithResult = result);

            coordinator.HandleCompileStoppedWithoutFinishEvent(500);

            Assert.That(abortedWithResult, Is.Not.Null);
            Assert.That(abortedWithResult.Success, Is.False);
            Assert.That(abortedWithResult.IsIndeterminate, Is.False);
            Assert.That(abortedWithResult.Errors[0].file, Is.EqualTo(asmdefPath));
        }

        [Test]
        public void HandleCompileStoppedWithoutFinishEvent_WhenNoAssemblyDefinitionErrorsExist_AbortsWithIndeterminateResult()
        {
            // Verifies missed-callback recovery keeps indeterminate status when nothing known explains the gap.
            CompilerMessage[] compileMessages =
            {
                new()
                {
                    type = CompilerMessageType.Error,
                    message = "CS0000: sample compile error",
                    file = "Assets/Scripts/Sample.cs",
                    line = 7
                }
            };
            CompileResult abortedWithResult = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: _ => new AssemblyDefinitionConsoleErrorResult(
                    new AssemblyDefinitionConsoleError[0]),
                getCompileMessages: () => compileMessages,
                getIsForceCompile: () => false,
                abortWithResult: result => abortedWithResult = result);

            coordinator.HandleCompileStoppedWithoutFinishEvent(500);

            Assert.That(abortedWithResult, Is.Not.Null);
            Assert.That(abortedWithResult.Success, Is.Null);
            Assert.That(abortedWithResult.IsIndeterminate, Is.True);
            Assert.That(abortedWithResult.Errors[0].message, Is.EqualTo("CS0000: sample compile error"));
        }

        [Test]
        public void HandleCompileStoppedWithoutFinishEvent_WhenConsoleErrorsExist_AppendsSummaryToIndeterminateMessage()
        {
            // Verifies the indeterminate message keeps its wording and get-logs pointer while appending
            // the recent Console errors so the cause is visible without a second round trip.
            const string consoleError =
                "Assembly has duplicate references: UnityEngine.TestRunner,UnityEditor.TestRunner";
            CompileResult abortedWithResult = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                getConsoleErrorEntries: () => new[]
                {
                    new UnityCliLoopConsoleLogEntry(UnityCliLoopLogType.Error, consoleError, string.Empty)
                },
                abortWithResult: result => abortedWithResult = result);

            coordinator.HandleCompileStoppedWithoutFinishEvent(500);

            Assert.That(abortedWithResult, Is.Not.Null);
            Assert.That(abortedWithResult.IsIndeterminate, Is.True);
            Assert.That(
                abortedWithResult.Message,
                Is.EqualTo(
                    "Unity stopped compiling before Unity CLI Loop received the compilationFinished callback. " +
                    "The compile result is indeterminate; use get-logs to inspect the compiler output.\n" +
                    "Recent Console errors:\n- " + consoleError));
        }

        [Test]
        public void HandleCompileStoppedWithoutFinishEvent_WhenConsoleErrorsPredateCompileStart_OmitsThemFromSummary()
        {
            // Verifies errors logged before the compile request started are not presented as its cause,
            // while the asmdef validation still sees the full Console snapshot.
            const string staleError = "NullReferenceException from an earlier Play session";
            const string freshError = "Assembly has duplicate references: UnityEngine.TestRunner,UnityEditor.TestRunner";
            UnityCliLoopConsoleLogEntry[] validatedEntries = null;
            CompileResult abortedWithResult = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                findAssemblyDefinitionErrors: entries =>
                {
                    validatedEntries = entries;
                    return new AssemblyDefinitionConsoleErrorResult(new AssemblyDefinitionConsoleError[0]);
                },
                getConsoleErrorEntries: () => new[]
                {
                    new UnityCliLoopConsoleLogEntry(UnityCliLoopLogType.Error, staleError, string.Empty),
                    new UnityCliLoopConsoleLogEntry(UnityCliLoopLogType.Error, freshError, string.Empty)
                },
                getConsoleErrorCountAtCompileStart: () => 1,
                abortWithResult: result => abortedWithResult = result);

            coordinator.HandleCompileStoppedWithoutFinishEvent(500);

            Assert.That(abortedWithResult.Message, Does.EndWith("Recent Console errors:\n- " + freshError));
            Assert.That(abortedWithResult.Message, Does.Not.Contain(staleError));
            Assert.That(validatedEntries, Is.Not.Null);
            Assert.That(validatedEntries.Length, Is.EqualTo(2));
        }

        [Test]
        public void HandleCompileStoppedWithoutFinishEvent_WhenNoConsoleErrorsExist_KeepsIndeterminateMessageUnchanged()
        {
            // Verifies an empty Console leaves the indeterminate message exactly as before.
            CompileResult abortedWithResult = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                abortWithResult: result => abortedWithResult = result);

            coordinator.HandleCompileStoppedWithoutFinishEvent(500);

            Assert.That(
                abortedWithResult.Message,
                Is.EqualTo(
                    "Unity stopped compiling before Unity CLI Loop received the compilationFinished callback. " +
                    "The compile result is indeterminate; use get-logs to inspect the compiler output."));
        }

        /// <summary>
        /// Verifies an assembly-progress stall warning never aborts the compile request.
        /// </summary>
        [Test]
        public void HandleAssemblyProgressStalled_WhenInvoked_DoesNotAbort()
        {
            CompileResult abortedWithResult = null;
            string abortedWithMessage = null;
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                abortWithResult: result => abortedWithResult = result,
                abort: message => abortedWithMessage = message);

            coordinator.HandleAssemblyProgressStalled(300000);

            Assert.That(abortedWithResult, Is.Null);
            Assert.That(abortedWithMessage, Is.Null);
        }

        /// <summary>
        /// Verifies the stall handler emits compile_assembly_progress_stalled with the required context keys.
        /// </summary>
        [Test]
        public void HandleAssemblyProgressStalled_WhenInvoked_LogsCompileAssemblyProgressStalledContext()
        {
            CompilerMessage[] compileMessages =
            {
                new()
                {
                    type = CompilerMessageType.Error,
                    message = "CS0000: sample compile error",
                    file = "Assets/Scripts/Sample.cs",
                    line = 7
                }
            };
            CompileLifecycleRecoveryCoordinator coordinator = CreateCoordinator(
                getCompileMessages: () => compileMessages,
                getAssemblyFinishedCount: () => 2);

            VibeLogger.ClearMemoryLogs();
            coordinator.HandleAssemblyProgressStalled(300000);

            JArray entries = JArray.Parse(VibeLogger.GetLogsForAi());
            JObject stallLog = null;
            foreach (JToken token in entries)
            {
                JObject entry = (JObject)token;
                if ((string)entry["operation"] == "compile_assembly_progress_stalled")
                {
                    stallLog = entry;
                    break;
                }
            }

            Assert.That(stallLog, Is.Not.Null);
            JObject context = (JObject)stallLog["context"];
            Assert.That(context, Is.Not.Null);
            Assert.That((int)context["stalled_ms"], Is.EqualTo(300000));
            Assert.That((int)context["assembly_finished_count"], Is.EqualTo(2));
            Assert.That((int)context["message_count"], Is.EqualTo(1));
            Assert.That(context["editor_compiling"].Type, Is.EqualTo(JTokenType.Boolean));
            Assert.That(context["editor_updating"].Type, Is.EqualTo(JTokenType.Boolean));
        }

        private static CompileLifecycleRecoveryCoordinator CreateCoordinator(
            Func<UnityCliLoopConsoleLogEntry[], AssemblyDefinitionConsoleErrorResult> findAssemblyDefinitionErrors = null,
            Func<UnityCliLoopConsoleLogEntry[]> getConsoleErrorEntries = null,
            Func<int> getConsoleErrorCountAtCompileStart = null,
            Func<ValidationResult> validateNoDuplicateAsmdefNames = null,
            Func<CompilerMessage[]> getCompileMessages = null,
            Func<int> getAssemblyFinishedCount = null,
            Func<bool> getIsForceCompile = null,
            Action<CompileResult> abortWithResult = null,
            Action<string> abort = null)
        {
            return new CompileLifecycleRecoveryCoordinator(
                isEditorCompiling: () => false,
                isRequestCompleted: () => false,
                getCurrentCompileTask: () => null,
                findAssemblyDefinitionErrors: findAssemblyDefinitionErrors ??
                    (_ => new AssemblyDefinitionConsoleErrorResult(new AssemblyDefinitionConsoleError[0])),
                getConsoleErrorEntries: getConsoleErrorEntries ?? (() => new UnityCliLoopConsoleLogEntry[0]),
                getConsoleErrorCountAtCompileStart: getConsoleErrorCountAtCompileStart ?? (() => 0),
                validateNoDuplicateAsmdefNames: validateNoDuplicateAsmdefNames ?? ValidationResult.Success,
                getIsForceCompile: getIsForceCompile ?? (() => false),
                getCompileMessages: getCompileMessages ?? (() => new CompilerMessage[0]),
                getAssemblyFinishedCount: getAssemblyFinishedCount ?? (() => 0),
                getMonotonicSeconds: () => 0d,
                buildStateContext: context => context,
                abortWithResult: abortWithResult ?? (_ => { }),
                abort: abort ?? (_ => { }));
        }
    }
}
