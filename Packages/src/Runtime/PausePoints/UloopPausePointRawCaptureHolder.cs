#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Retains raw captured variable references for the latest pause-point hit only.
    /// Why: execute-dynamic-code can inspect live objects while Unity stays paused; references
    /// must not outlive that paused window.
    /// </summary>
    internal static class UloopPausePointRawCaptureHolder
    {
        private static UloopPausePointRawCaptureSnapshot _latest;

        internal static void Store(UloopPausePointCapturedVariableFrame frame, string pausePointId)
        {
            Dictionary<string, object> valuesByName = new();
            foreach (UloopPausePointCapturedVariableEntry entry in frame.Entries)
            {
                valuesByName[entry.Name] = entry.Value;
            }

            UloopPausePointRawCaptureSnapshot snapshot = new(pausePointId, valuesByName);
            Interlocked.Exchange(ref _latest, snapshot);
        }

        internal static void Clear()
        {
            Interlocked.Exchange(ref _latest, null);
        }

        internal static (bool Found, object Value) TryGetCapturedValue(string name)
        {
            UloopPausePointRawCaptureSnapshot snapshot = Volatile.Read(ref _latest);
            if (snapshot == null)
            {
                return (false, null);
            }

            if (!snapshot.ValuesByName.TryGetValue(name, out object value))
            {
                return (false, null);
            }

            return (true, value);
        }

        internal static IReadOnlyList<string> GetCapturedNames()
        {
            UloopPausePointRawCaptureSnapshot snapshot = Volatile.Read(ref _latest);
            if (snapshot == null)
            {
                return System.Array.Empty<string>();
            }

            return snapshot.Names;
        }

        internal static string GetCapturedPausePointId()
        {
            UloopPausePointRawCaptureSnapshot snapshot = Volatile.Read(ref _latest);
            return snapshot?.PausePointId ?? string.Empty;
        }

        private sealed class UloopPausePointRawCaptureSnapshot
        {
            public UloopPausePointRawCaptureSnapshot(string pausePointId, Dictionary<string, object> valuesByName)
            {
                PausePointId = pausePointId;
                ValuesByName = valuesByName;
                Names = new List<string>(valuesByName.Keys);
            }

            public string PausePointId { get; }
            public IReadOnlyDictionary<string, object> ValuesByName { get; }
            public IReadOnlyList<string> Names { get; }
        }
    }
}
#endif
