#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that the log filter drops only the messageless Input System assertion and forwards everything else.
    /// </summary>
    public class InputSystemMonitorRemovalAssertionLogFilterTests
    {
        /// <summary>
        /// Verifies the messageless "Assertion failed" assert is suppressed and counted.
        /// </summary>
        [Test]
        public void LogFormat_WhenMessagelessAssertion_SuppressesAndCounts()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);

            filter.LogFormat(LogType.Assert, null, "{0}", "Assertion failed");

            Assert.That(inner.FormattedLogs, Is.Empty);
            Assert.That(filter.SuppressedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an assert carrying a custom message is forwarded and not counted.
        /// </summary>
        [Test]
        public void LogFormat_WhenAssertionHasMessage_Forwards()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);

            filter.LogFormat(LogType.Assert, null, "{0}", "Assertion failed: custom message");

            Assert.That(inner.FormattedLogs.Count, Is.EqualTo(1));
            Assert.That(filter.SuppressedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an Error-level log with the same text is forwarded and not counted.
        /// </summary>
        [Test]
        public void LogFormat_WhenErrorWithAssertionText_Forwards()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);

            filter.LogFormat(LogType.Error, null, "{0}", "Assertion failed");

            Assert.That(inner.FormattedLogs.Count, Is.EqualTo(1));
            Assert.That(filter.SuppressedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an assert with more than one format argument is forwarded.
        /// </summary>
        [Test]
        public void LogFormat_WhenAssertionHasTwoArgs_Forwards()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);

            filter.LogFormat(LogType.Assert, null, "{0}", "Assertion failed", "extra");

            Assert.That(inner.FormattedLogs.Count, Is.EqualTo(1));
            Assert.That(filter.SuppressedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies an assert whose format does not render to exactly "Assertion failed" is forwarded.
        /// </summary>
        [Test]
        public void LogFormat_WhenAssertionFormatIsCustom_Forwards()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);

            filter.LogFormat(LogType.Assert, null, "custom: {0}", "Assertion failed");

            Assert.That(inner.FormattedLogs.Count, Is.EqualTo(1));
            Assert.That(filter.SuppressedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies exceptions are always forwarded to the inner handler.
        /// </summary>
        [Test]
        public void LogException_Always_Forwards()
        {
            RecordingLogHandler inner = new RecordingLogHandler();
            InputSystemMonitorRemovalAssertionLogFilter filter = new InputSystemMonitorRemovalAssertionLogFilter(inner);
            InvalidOperationException exception = new InvalidOperationException("boom");

            filter.LogException(exception, null);

            Assert.That(inner.Exceptions, Is.EqualTo(new Exception[] { exception }));
            Assert.That(filter.SuppressedCount, Is.EqualTo(0));
        }

        private sealed class RecordingLogHandler : ILogHandler
        {
            public readonly List<string> FormattedLogs = new List<string>();
            public readonly List<Exception> Exceptions = new List<Exception>();

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                FormattedLogs.Add(logType + ":" + string.Format(format, args));
            }

            public void LogException(Exception exception, Object context)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
#endif
