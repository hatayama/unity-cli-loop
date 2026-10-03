using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Sets aside the last completed recording kept in SessionState before a test and puts it back afterwards,
    /// so running the tests does not lose a recording the user has not been told about yet.
    /// </summary>
    internal static class LastCompletedRecordingTestState
    {
        internal static LastCompletedRecording TakeAndClear()
        {
            LastCompletedRecording previous = LastCompletedRecordingStore.TryRead();
            LastCompletedRecordingStore.Clear();
            return previous;
        }

        internal static void Restore(LastCompletedRecording previous)
        {
            LastCompletedRecordingStore.Clear();
            if (!previous.HasValue)
            {
                return;
            }

            LastCompletedRecordingStore.Save(previous.Snapshot);
            if (previous.IsReported)
            {
                LastCompletedRecordingStore.MarkReported();
            }
        }
    }
}
