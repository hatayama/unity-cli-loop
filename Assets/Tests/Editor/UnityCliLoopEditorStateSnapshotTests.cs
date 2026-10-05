using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the compile state cached for answers built outside the Editor main thread.
    /// </summary>
    [TestFixture]
    public sealed class UnityCliLoopEditorStateSnapshotTests
    {
        [TearDown]
        public void TearDown()
        {
            UnityCliLoopEditorStateSnapshot.ClearForTesting();
        }

        /// <summary>
        /// What: a cleared cache reports that no compile state has been recorded yet.
        /// </summary>
        [Test]
        public void GetCompileState_AfterClear_ReportsNoValue()
        {
            UnityCliLoopEditorStateSnapshot.ClearForTesting();

            (bool HasValue, bool IsCompiling, bool IsUpdating) compileState =
                UnityCliLoopEditorStateSnapshot.GetCompileState();

            Assert.That(compileState.HasValue, Is.False);
        }

        /// <summary>
        /// What: a recorded compile state is read back with the compiling and updating values as set.
        /// </summary>
        [Test]
        public void GetCompileState_AfterSet_ReturnsRecordedValues()
        {
            UnityCliLoopEditorStateSnapshot.ClearForTesting();
            UnityCliLoopEditorStateSnapshot.SetCompileStateForTesting(isCompiling: true, isUpdating: false);

            (bool HasValue, bool IsCompiling, bool IsUpdating) compileState =
                UnityCliLoopEditorStateSnapshot.GetCompileState();

            Assert.That(compileState.HasValue, Is.True);
            Assert.That(compileState.IsCompiling, Is.True);
            Assert.That(compileState.IsUpdating, Is.False);
        }

        /// <summary>
        /// What: clearing the cache drops a recorded compile state back to no value.
        /// </summary>
        [Test]
        public void ClearForTesting_AfterCompileStateWasSet_ResetsToNoValue()
        {
            UnityCliLoopEditorStateSnapshot.SetCompileStateForTesting(isCompiling: true, isUpdating: true);

            UnityCliLoopEditorStateSnapshot.ClearForTesting();
            (bool HasValue, bool IsCompiling, bool IsUpdating) compileState =
                UnityCliLoopEditorStateSnapshot.GetCompileState();

            Assert.That(compileState.HasValue, Is.False);
            Assert.That(compileState.IsCompiling, Is.False);
            Assert.That(compileState.IsUpdating, Is.False);
        }
    }
}
