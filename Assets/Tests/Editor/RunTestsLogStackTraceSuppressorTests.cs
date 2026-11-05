using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Run Tests Log Stack Trace Suppressor behavior.
    /// </summary>
    public class RunTestsLogStackTraceSuppressorTests
    {
        private static readonly LogType[] AllLogTypes = (LogType[])Enum.GetValues(typeof(LogType));

        private FakeRunTestsLogStackTraceSettings _settings;
        private RunTestsLogStackTraceSuppressor _suppressor;

        [SetUp]
        public void SetUp()
        {
            _settings = new FakeRunTestsLogStackTraceSettings();
            _suppressor = new RunTestsLogStackTraceSuppressor(_settings);
        }

        /// <summary>
        /// Verifies every log type stops carrying stack traces, since any of them can flood a run.
        /// </summary>
        [Test]
        public void Suppress_TurnsEveryLogTypeOff()
        {
            _suppressor.Suppress();

            foreach (LogType logType in AllLogTypes)
            {
                Assert.That(_settings.Get(logType), Is.EqualTo(StackTraceLogType.None), logType.ToString());
            }
        }

        /// <summary>
        /// Verifies each log type gets back its own value, not one shared default.
        /// </summary>
        [Test]
        public void RestoreIfSaved_AfterSuppress_PutsBackEachLogTypesOwnValue()
        {
            Dictionary<LogType, StackTraceLogType> original = _settings.Snapshot();

            _suppressor.Suppress();
            _suppressor.RestoreIfSaved();

            Assert.That(_settings.Snapshot(), Is.EqualTo(original));
            Assert.That(_settings.HasSaved, Is.False);
        }

        /// <summary>
        /// Verifies a restore with nothing saved leaves settings the project changed on its own alone.
        /// </summary>
        [Test]
        public void RestoreIfSaved_WithNothingSaved_ChangesNothing()
        {
            _suppressor.RestoreIfSaved();

            Assert.That(_settings.SetCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a run abandoned while suppressed cannot make None look like the project's own setting.
        /// </summary>
        [Test]
        public void Suppress_AfterAbandonedRun_KeepsTheProjectsOriginalValues()
        {
            Dictionary<LogType, StackTraceLogType> original = _settings.Snapshot();

            _suppressor.Suppress();
            _suppressor.Suppress();
            _suppressor.RestoreIfSaved();

            Assert.That(_settings.Snapshot(), Is.EqualTo(original));
        }

        /// <summary>
        /// Verifies a failure partway through changing the settings can still be undone, because the save
        /// comes first. Why fail on a later call: failing on the first one changes nothing, so a restore
        /// with nothing saved would pass too.
        /// </summary>
        [Test]
        public void Suppress_WhenChangingSettingsFailsPartway_CanStillBeRestored()
        {
            Dictionary<LogType, StackTraceLogType> original = _settings.Snapshot();
            _settings.FailOnSetCall = 3;

            Assert.Throws<InvalidOperationException>(() => _suppressor.Suppress());
            _suppressor.RestoreIfSaved();

            Assert.That(_settings.Snapshot(), Is.EqualTo(original));
        }
    }

    /// <summary>
    /// In-memory stand-in for Unity's stack trace settings, so tests never change the Editor running them.
    /// </summary>
    internal sealed class FakeRunTestsLogStackTraceSettings : IRunTestsLogStackTraceSettings
    {
        // Why different values: a restore that used one default for every type must fail the tests.
        private readonly Dictionary<LogType, StackTraceLogType> _current = new()
        {
            { LogType.Error, StackTraceLogType.Full },
            { LogType.Assert, StackTraceLogType.ScriptOnly },
            { LogType.Warning, StackTraceLogType.None },
            { LogType.Log, StackTraceLogType.ScriptOnly },
            { LogType.Exception, StackTraceLogType.Full },
        };

        private int[] _saved = Array.Empty<int>();

        /// <summary>
        /// One-based number of the Set call that throws, or 0 for none.
        /// </summary>
        public int FailOnSetCall { get; set; }

        public int SetCallCount { get; private set; }
        public bool HasSaved => _saved.Length > 0;

        public Dictionary<LogType, StackTraceLogType> Snapshot()
        {
            return new Dictionary<LogType, StackTraceLogType>(_current);
        }

        public StackTraceLogType Get(LogType logType)
        {
            return _current[logType];
        }

        public void Set(LogType logType, StackTraceLogType stackTraceLogType)
        {
            SetCallCount++;
            if (SetCallCount == FailOnSetCall)
            {
                throw new InvalidOperationException("Set failed on purpose.");
            }

            _current[logType] = stackTraceLogType;
        }

        public int[] LoadSaved()
        {
            return _saved;
        }

        public void Save(int[] savedStackTraceLogTypes)
        {
            _saved = savedStackTraceLogTypes;
        }

        public void ClearSaved()
        {
            _saved = Array.Empty<int>();
        }
    }
}
