#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Reflection;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves TestRunnerApi cancel helpers once via reflection and caches the result.
    /// Supports TF 1.3.9 (internal CancelTestRun) and TF 1.4+ (public CancelTestRun).
    /// </summary>
    internal static class TestRunnerApiCancelBridge
    {
        private static readonly object ResolveLock = new object();
        // Why volatile: double-checked locking outside the lock must observe a completed resolve.
        private static volatile bool _resolved;
        private static MethodInfo _cancelTestRunMethod;
        private static MethodInfo _isRunActiveMethod;
        private static string _resolveLog;

        /// <summary>
        /// True when CancelTestRun(string) was resolved.
        /// </summary>
        internal static bool HasCancelTestRun
        {
            get
            {
                EnsureResolved();
                return _cancelTestRunMethod != null;
            }
        }

        /// <summary>
        /// True when parameterless IsRunActive() was resolved.
        /// </summary>
        internal static bool HasIsRunActive
        {
            get
            {
                EnsureResolved();
                return _isRunActiveMethod != null;
            }
        }

        /// <summary>
        /// Invokes CancelTestRun(guid) when available. Returns false when unavailable or invoke fails.
        /// </summary>
        internal static bool TryCancelTestRun(string runGuid)
        {
            EnsureResolved();
            if (_cancelTestRunMethod == null || string.IsNullOrEmpty(runGuid))
            {
                return false;
            }

            object result = _cancelTestRunMethod.Invoke(null, new object[] { runGuid });
            return result is bool canceled && canceled;
        }

        /// <summary>
        /// Invokes parameterless IsRunActive() when available. Returns false when unavailable.
        /// </summary>
        internal static bool TryIsRunActive()
        {
            EnsureResolved();
            if (_isRunActiveMethod == null)
            {
                return false;
            }

            object result = _isRunActiveMethod.Invoke(null, Array.Empty<object>());
            return result is bool isActive && isActive;
        }

        /// <summary>
        /// Resolves methods once. Failures are cached so later calls stay on the Option B path.
        /// </summary>
        internal static void EnsureResolved()
        {
            if (_resolved)
            {
                return;
            }

            lock (ResolveLock)
            {
                if (_resolved)
                {
                    return;
                }

                (MethodInfo cancel, MethodInfo isRunActive, string log) =
                    TestRunnerApiCancelMethodLookup.Resolve(typeof(TestRunnerApi));
                _cancelTestRunMethod = cancel;
                _isRunActiveMethod = isRunActive;
                _resolveLog = log;
                _resolved = true;

                if (!string.IsNullOrEmpty(log))
                {
                    Debug.LogWarning(log);
                }
            }
        }
    }
}
#endif
