using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Unit tests for LogFilteringService
    /// Related classes: LogFilteringService, GetLogsUseCase, LogEntry
    /// </summary>
    [TestFixture]
    public class LogFilteringServiceTests
    {
        private LogFilteringService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new LogFilteringService();
        }

        private static UnityCliLoopConsoleLogEntry CreateEntry(string message)
        {
            return new UnityCliLoopConsoleLogEntry(UnityCliLoopLogType.Log, message, $"stack of {message}");
        }

        /// <summary>
        /// Verifies entries are returned newest-first (input order reversed)
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_ReturnsEntriesNewestFirst()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("oldest"), CreateEntry("middle"), CreateEntry("newest") };

            LogEntry[] result = _service.FilterAndLimitLogs(entries, 10, includeStackTrace: false);

            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result[0].Message, Is.EqualTo("newest"));
            Assert.That(result[1].Message, Is.EqualTo("middle"));
            Assert.That(result[2].Message, Is.EqualTo("oldest"));
        }

        /// <summary>
        /// Verifies maxCount keeps only the newest entries when input exceeds the limit
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_LimitsToNewestEntries()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("first"), CreateEntry("second"), CreateEntry("third"), CreateEntry("fourth") };

            LogEntry[] result = _service.FilterAndLimitLogs(entries, 2, includeStackTrace: false);

            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result[0].Message, Is.EqualTo("fourth"));
            Assert.That(result[1].Message, Is.EqualTo("third"));
        }

        /// <summary>
        /// Verifies stack traces are included when includeStackTrace is true
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_IncludesStackTraceWhenRequested()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("message") };

            LogEntry[] result = _service.FilterAndLimitLogs(entries, 10, includeStackTrace: true);

            Assert.That(result[0].StackTrace, Is.EqualTo("stack of message"));
        }

        /// <summary>
        /// Verifies stack traces are null when includeStackTrace is false
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_OmitsStackTraceWhenNotRequested()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("message") };

            LogEntry[] result = _service.FilterAndLimitLogs(entries, 10, includeStackTrace: false);

            Assert.That(result[0].StackTrace, Is.Null);
        }

        /// <summary>
        /// Verifies maxCount of zero returns an empty array
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_WithZeroMaxCount_ReturnsEmptyArray()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("message") };

            LogEntry[] result = _service.FilterAndLimitLogs(entries, 0, includeStackTrace: false);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies empty input returns an empty array
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_WithEmptyInput_ReturnsEmptyArray()
        {
            LogEntry[] result = _service.FilterAndLimitLogs(Array.Empty<UnityCliLoopConsoleLogEntry>(), 10, includeStackTrace: false);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies null entries are rejected with ArgumentNullException
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_WithNullEntries_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _service.FilterAndLimitLogs(null, 10, includeStackTrace: false));
        }

        /// <summary>
        /// Verifies negative maxCount is rejected with ArgumentOutOfRangeException
        /// </summary>
        [Test]
        public void FilterAndLimitLogs_WithNegativeMaxCount_ThrowsArgumentOutOfRangeException()
        {
            UnityCliLoopConsoleLogEntry[] entries = { CreateEntry("message") };

            Assert.Throws<ArgumentOutOfRangeException>(() => _service.FilterAndLimitLogs(entries, -1, includeStackTrace: false));
        }
    }
}
