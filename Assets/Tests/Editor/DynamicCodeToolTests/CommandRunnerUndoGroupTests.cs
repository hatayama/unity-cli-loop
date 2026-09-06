using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using DynamicExecutionContext = io.github.hatayama.UnityCliLoop.FirstPartyTools.ExecutionContext;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies CommandRunner closes its Undo group instead of leaving an open recording.
    /// </summary>
    [TestFixture]
    public class CommandRunnerUndoGroupTests
    {
        private const int StubUndoGroup = 42;

        [TearDown]
        public void TearDown()
        {
            UloopDynamicCodePartialResults.Clear();
            DestroyRecordedObject();
        }

        /// <summary>
        /// What: a completed command collapses the group captured at begin and then increments it, without naming it.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandCompletes_CollapsesThenIncrementsUndoGroupWithoutNamingIt()
        {
            List<string> calls = new();
            CommandRunnerUndoHooks hooks = CreateRecordingHooks(calls);
            WrappedDynamicCommandState.PrepareReturningCommand("ready");
            CommandRunner runner = new(DynamicCodeServices.CommandEntryPointResolver, hooks);

            ExecutionResult result = await runner.ExecuteAsync(CreateContext());

            Assert.That(result.Success, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "GetCurrentGroup", "Collapse:" + StubUndoGroup, "Increment" }));
        }

        /// <summary>
        /// What: a throwing command still collapses and then increments the Undo group.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandThrows_StillCollapsesThenIncrementsUndoGroup()
        {
            List<string> calls = new();
            CommandRunnerUndoHooks hooks = CreateRecordingHooks(calls);
            WrappedDynamicCommandState.PrepareThrowingCommand("beforeThrow", "saved");
            CommandRunner runner = new(DynamicCodeServices.CommandEntryPointResolver, hooks);

            ExecutionResult result = await runner.ExecuteAsync(CreateContext());

            Assert.That(result.Success, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "GetCurrentGroup", "Collapse:" + StubUndoGroup, "Increment" }));
        }

        /// <summary>
        /// What: a command that records nothing leaves no pending Undo recording behind (issue #2626).
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandRecordsNothing_LeavesNoPendingUndoRecording()
        {
            MethodInfo hasUndoRecordObjects = GetHasUndoRecordObjectsMethod();

            // Why increment first: earlier tests or editor interaction may have left a group open,
            // and this test can only prove anything when it starts from a closed group.
            Undo.IncrementCurrentGroup();
            Assume.That((bool)hasUndoRecordObjects.Invoke(null, null), Is.False, "precondition");

            WrappedDynamicCommandState.PrepareReturningCommand("ready");
            CommandRunner runner = new();

            ExecutionResult result = await runner.ExecuteAsync(CreateContext());

            Assert.That(result.Success, Is.True);
            Assert.That((bool)hasUndoRecordObjects.Invoke(null, null), Is.False);
        }

        /// <summary>
        /// What: a command that does record through the Undo API still leaves no pending recording behind.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenCommandRecordsUndo_LeavesNoPendingUndoRecording()
        {
            MethodInfo hasUndoRecordObjects = GetHasUndoRecordObjectsMethod();

            // Why increment first: earlier tests or editor interaction may have left a group open,
            // and this test can only prove anything when it starts from a closed group.
            Undo.IncrementCurrentGroup();
            Assume.That((bool)hasUndoRecordObjects.Invoke(null, null), Is.False, "precondition");

            WrappedDynamicCommandState.PrepareUndoRecordingCommand();
            CommandRunner runner = new();

            ExecutionResult result = await runner.ExecuteAsync(CreateContext());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Result, Is.EqualTo("recorded"));
            Assert.That(WrappedDynamicCommandState.RecordedObject, Is.Not.Null, "the command must have recorded");
            Assert.That((bool)hasUndoRecordObjects.Invoke(null, null), Is.False);
        }

        private static void DestroyRecordedObject()
        {
            ScriptableObject recorded = WrappedDynamicCommandState.RecordedObject;
            WrappedDynamicCommandState.ClearRecordedObject();
            if (recorded == null)
            {
                return;
            }

            Undo.ClearUndo(recorded);
            UnityEngine.Object.DestroyImmediate(recorded);
        }

        private static MethodInfo GetHasUndoRecordObjectsMethod()
        {
            MethodInfo hasUndoRecordObjects = typeof(DrivenRectTransformTracker)
                .GetMethod("HasUndoRecordObjects", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(hasUndoRecordObjects, Is.Not.Null, "DrivenRectTransformTracker.HasUndoRecordObjects must exist");
            return hasUndoRecordObjects;
        }

        private static CommandRunnerUndoHooks CreateRecordingHooks(List<string> calls)
        {
            return new CommandRunnerUndoHooks
            {
                GetCurrentGroup = () =>
                {
                    calls.Add("GetCurrentGroup");
                    return StubUndoGroup;
                },
                CollapseUndoOperations = group => calls.Add("Collapse:" + group),
                IncrementCurrentGroup = () => calls.Add("Increment")
            };
        }

        private static DynamicExecutionContext CreateContext()
        {
            return new DynamicExecutionContext
            {
                CompiledAssembly = typeof(global::UnityCliLoop.Dynamic.DynamicCommand).Assembly,
                CancellationToken = CancellationToken.None
            };
        }
    }
}
