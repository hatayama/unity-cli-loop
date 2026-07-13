using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Injects Undo group operations for CommandRunner so pure C# tests can force begin/end failures.
    /// </summary>
    internal sealed class CommandRunnerUndoHooks
    {
        public Func<int> GetCurrentGroup { get; set; }

        public Action<string> SetCurrentGroupName { get; set; }

        public Action<int> CollapseUndoOperations { get; set; }
    }
}
