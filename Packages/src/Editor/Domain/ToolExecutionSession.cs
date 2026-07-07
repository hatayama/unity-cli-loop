using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Owns the thread-safe single-flight state for tool execution sessions.
    /// </summary>
    internal sealed class ToolExecutionSession
    {
        private const string UnknownToolName = "unknown";

        private readonly object _executionStateLock = new();
        private string _runningToolName;
        private int _runningExecutionCount;

        internal ToolExecutionSessionEnterResult TryEnter(string requestedToolName)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");

            lock (_executionStateLock)
            {
                if (_runningExecutionCount == 0)
                {
                    _runningToolName = requestedToolName;
                    _runningExecutionCount = 1;
                    return ToolExecutionSessionEnterResult.Entered();
                }

                if (CanShareExecutionSlot(_runningToolName, requestedToolName))
                {
                    _runningExecutionCount++;
                    return ToolExecutionSessionEnterResult.Entered();
                }

                return ToolExecutionSessionEnterResult.Busy(GetRunningToolNameInsideLock());
            }
        }

        internal void Exit()
        {
            lock (_executionStateLock)
            {
                Debug.Assert(_runningExecutionCount > 0, "running execution count must be positive before exit");
                _runningExecutionCount--;
                if (_runningExecutionCount > 0)
                {
                    return;
                }

                _runningToolName = null;
            }
        }

        private static bool CanShareExecutionSlot(string runningToolName, string requestedToolName)
        {
            return runningToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE
                   && requestedToolName == UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;
        }

        private string GetRunningToolNameInsideLock()
        {
            return string.IsNullOrWhiteSpace(_runningToolName)
                ? UnknownToolName
                : _runningToolName;
        }
    }

    /// <summary>
    /// Reports whether a tool execution request entered the session or was rejected by the single-flight gate.
    /// </summary>
    internal readonly struct ToolExecutionSessionEnterResult
    {
        public readonly bool IsEntered;
        public readonly string RunningToolName;

        private ToolExecutionSessionEnterResult(bool isEntered, string runningToolName)
        {
            Debug.Assert(isEntered || !string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace for busy decisions");

            IsEntered = isEntered;
            RunningToolName = runningToolName;
        }

        public static ToolExecutionSessionEnterResult Entered()
        {
            return new ToolExecutionSessionEnterResult(true, string.Empty);
        }

        public static ToolExecutionSessionEnterResult Busy(string runningToolName)
        {
            return new ToolExecutionSessionEnterResult(false, runningToolName);
        }
    }
}
