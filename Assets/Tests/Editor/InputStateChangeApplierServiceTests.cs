#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that applying input state suppresses only the Input System's monitor-removal assertion
    /// and always restores the global log handler.
    /// </summary>
    public class InputStateChangeApplierServiceTests
    {
        private const int MonitorIndex = 7;

        private Keyboard _keyboard;
        private int _assertLogCount;
        private int _warningLogCount;

        [SetUp]
        public void SetUp()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>("InputStateChangeApplierServiceTestsKeyboard");
            _assertLogCount = 0;
            _warningLogCount = 0;
            UnityEngine.Application.logMessageReceived += CountLog;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Application.logMessageReceived -= CountLog;
            InputSystem.RemoveDevice(_keyboard);
        }

        /// <summary>
        /// Verifies a monitor that removes itself during notification produces no assertion log and is counted.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorRemovesItself_SuppressesAssertionAndCountsIt()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            SelfRemovingMonitor monitor = new SelfRemovingMonitor();
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);

            ApplyKeyB(service);

            Assert.That(monitor.NotifiedCount, Is.EqualTo(1));
            Assert.That(_assertLogCount, Is.EqualTo(0));
            Assert.That(service.SuppressedAssertionCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the global log handler is the same instance before and after Apply.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorRemovesItself_RestoresOriginalLogHandler()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            SelfRemovingMonitor monitor = new SelfRemovingMonitor();
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            ILogHandler original = Debug.unityLogger.logHandler;

            ApplyKeyB(service);

            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(original));
        }

        /// <summary>
        /// Verifies the global log handler is restored even when a monitor throws during notification.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorThrows_RestoresOriginalLogHandler()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            ThrowingMonitor monitor = new ThrowingMonitor();
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            ILogHandler original = Debug.unityLogger.logHandler;
            LogAssert.Expect(LogType.Error, new Regex("thrown from state change monitor"));
            LogAssert.Expect(LogType.Exception, new Regex("monitor failure"));

            try
            {
                ApplyKeyB(service);
            }
            finally
            {
                InputState.RemoveChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            }

            Assert.That(Debug.unityLogger.logHandler, Is.SameAs(original));
        }

        /// <summary>
        /// Verifies logs unrelated to the monitor-removal assertion still reach the log listeners.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorLogsWarning_ForwardsWarning()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            WarningLoggingMonitor monitor = new WarningLoggingMonitor();
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);

            try
            {
                ApplyKeyB(service);
            }
            finally
            {
                InputState.RemoveChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            }

            Assert.That(_warningLogCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the suppression counter stays at zero when no monitor is removed during Apply.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorStaysRegistered_DoesNotCount()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            WarningLoggingMonitor monitor = new WarningLoggingMonitor();
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);

            try
            {
                ApplyKeyB(service);
            }
            finally
            {
                InputState.RemoveChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            }

            Assert.That(service.SuppressedAssertionCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a bare Debug.Assert(false) raised by a monitor callback stays visible and is not counted.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorRaisesBareAssert_KeepsAssertVisibleAndDoesNotCount()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            BareAssertMonitor monitor = new BareAssertMonitor(removeSelf: false);
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            LogAssert.Expect(LogType.Assert, "Assertion failed");

            try
            {
                ApplyKeyB(service);
            }
            finally
            {
                InputState.RemoveChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            }

            Assert.That(_assertLogCount, Is.EqualTo(1));
            Assert.That(service.SuppressedAssertionCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a monitor that raises a bare assert and then removes itself keeps only its own assert visible
        /// while the Input System's monitor-removal assert is suppressed and counted once.
        /// </summary>
        [Test]
        public void Apply_WhenMonitorRaisesBareAssertAndRemovesItself_KeepsOnlyUserAssertVisible()
        {
            InputStateChangeApplierService service = new InputStateChangeApplierService();
            BareAssertMonitor monitor = new BareAssertMonitor(removeSelf: true);
            InputState.AddChangeMonitor(_keyboard[Key.B], monitor, MonitorIndex);
            LogAssert.Expect(LogType.Assert, "Assertion failed");

            ApplyKeyB(service);

            Assert.That(_assertLogCount, Is.EqualTo(1));
            Assert.That(service.SuppressedAssertionCount, Is.EqualTo(1));
        }

        private void ApplyKeyB(InputStateChangeApplierService service)
        {
            using (StateEvent.From(_keyboard, out InputEventPtr eventPtr))
            {
                _keyboard[Key.B].WriteValueIntoEvent(1f, eventPtr);
                service.Apply(_keyboard, eventPtr, InputUpdateType.Dynamic);
            }
        }

        private void CountLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Assert)
            {
                _assertLogCount++;
            }

            if (type == LogType.Warning && condition == WarningLoggingMonitor.Message)
            {
                _warningLogCount++;
            }
        }

        private sealed class SelfRemovingMonitor : IInputStateChangeMonitor
        {
            public int NotifiedCount;

            public void NotifyControlStateChanged(InputControl control, double time, InputEventPtr eventPtr, long monitorIndex)
            {
                NotifiedCount++;
                InputState.RemoveChangeMonitor(control, this, monitorIndex);
            }

            public void NotifyTimerExpired(InputControl control, double time, long monitorIndex, int timerIndex)
            {
            }
        }

        private sealed class ThrowingMonitor : IInputStateChangeMonitor
        {
            public void NotifyControlStateChanged(InputControl control, double time, InputEventPtr eventPtr, long monitorIndex)
            {
                throw new InvalidOperationException("monitor failure");
            }

            public void NotifyTimerExpired(InputControl control, double time, long monitorIndex, int timerIndex)
            {
            }
        }

        private sealed class BareAssertMonitor : IInputStateChangeMonitor
        {
            private readonly bool _removeSelf;

            public BareAssertMonitor(bool removeSelf)
            {
                _removeSelf = removeSelf;
            }

            public void NotifyControlStateChanged(InputControl control, double time, InputEventPtr eventPtr, long monitorIndex)
            {
                Debug.Assert(false);
                if (_removeSelf)
                {
                    InputState.RemoveChangeMonitor(control, this, monitorIndex);
                }
            }

            public void NotifyTimerExpired(InputControl control, double time, long monitorIndex, int timerIndex)
            {
            }
        }

        private sealed class WarningLoggingMonitor : IInputStateChangeMonitor
        {
            public const string Message = "monitor-side log";

            public void NotifyControlStateChanged(InputControl control, double time, InputEventPtr eventPtr, long monitorIndex)
            {
                Debug.LogWarning(Message);
            }

            public void NotifyTimerExpired(InputControl control, double time, long monitorIndex, int timerIndex)
            {
            }
        }
    }
}
#endif
