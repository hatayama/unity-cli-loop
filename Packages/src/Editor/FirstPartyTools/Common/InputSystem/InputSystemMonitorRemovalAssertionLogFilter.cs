#if ULOOP_HAS_INPUT_SYSTEM
using System;
using UnityEngine;

using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Log handler that drops only the messageless "Assertion failed" log raised by the Input System
    /// and forwards every other log to the wrapped handler.
    /// Why: when an input callback removes a state monitor while uloop applies state outside event
    /// processing, the Input System's FireStateChangeNotifications removes the monitor immediately and
    /// DynamicBitfield.ClearBit then trips its range assert. Player builds strip that Debug.Assert, so
    /// dropping it keeps the Editor's observable result the same as a player's.
    /// </summary>
    internal sealed class InputSystemMonitorRemovalAssertionLogFilter : ILogHandler
    {
        private const string MessagelessAssertionText = "Assertion failed";
        // Debug.Assert(bool) routes through Logger.Log(LogType, object), which formats with "{0}".
        private const string SingleArgumentFormat = "{0}";

        private readonly ILogHandler _inner;

        public int SuppressedCount { get; private set; }

        public InputSystemMonitorRemovalAssertionLogFilter(ILogHandler inner)
        {
            Debug.Assert(inner != null, "inner must not be null");
            _inner = inner;
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            if (IsMessagelessAssertion(logType, format, args))
            {
                SuppressedCount++;
                return;
            }

            _inner.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, Object context)
        {
            _inner.LogException(exception, context);
        }

        // Exact matching keeps user asserts with a message visible; only the Input System's bare assert is dropped.
        private static bool IsMessagelessAssertion(LogType logType, string format, object[] args)
        {
            if (logType != LogType.Assert)
            {
                return false;
            }

            if (format != SingleArgumentFormat)
            {
                return false;
            }

            if (args == null || args.Length != 1)
            {
                return false;
            }

            return args[0] is string text && text == MessagelessAssertionText;
        }
    }
}
#endif
