using System;
using System.Reflection;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Frees EditMode test runs that have finished but are still reachable. Unity Test Framework leaves each
    /// run's runner subscribed to two static events, and that runner holds the whole test tree and its results
    /// until the next Domain Reload, so repeated runs without one grow the managed heap.
    /// </summary>
    internal sealed class RunTestsFinishedRunReleaser
    {
        private const string CallbacksTypeName =
            "UnityEngine.TestRunner.NUnitExtensions.Runner.EditModeTestCallbacks, UnityEngine.TestRunner";
        private const string RestoringTestContextPropertyName = "RestoringTestContext";
        private const string BeforeAssemblyReloadMethodName = "OnBeforeAssemblyReload";

        private readonly PropertyInfo _restoringTestContext;
        private readonly Action<AssemblyReloadEvents.AssemblyReloadCallback> _unsubscribeBeforeAssemblyReload;

        internal RunTestsFinishedRunReleaser(
            PropertyInfo restoringTestContext,
            Action<AssemblyReloadEvents.AssemblyReloadCallback> unsubscribeBeforeAssemblyReload)
        {
            _restoringTestContext = restoringTestContext;
            _unsubscribeBeforeAssemblyReload = unsubscribeBeforeAssemblyReload
                ?? throw new ArgumentNullException(nameof(unsubscribeBeforeAssemblyReload));
        }

        internal static RunTestsFinishedRunReleaser CreateDefault()
        {
            PropertyInfo restoringTestContext = FindRestoringTestContextProperty();
            if (restoringTestContext == null)
            {
                VibeLogger.LogWarning(
                    "run_tests_finished_run_event_missing",
                    "This Test Framework version has no usable event to release finished test runs from.",
                    new { callbacksType = CallbacksTypeName });
            }

            return new RunTestsFinishedRunReleaser(
                restoringTestContext,
                callback => AssemblyReloadEvents.beforeAssemblyReload -= callback);
        }

        // Why by name: the class is internal to the Test Framework. A version that renames it only keeps the
        // memory growth, which must not break the test run reporting its result.
        internal static PropertyInfo FindRestoringTestContextProperty()
        {
            Type callbacksType = Type.GetType(CallbacksTypeName, throwOnError: false);
            if (callbacksType == null)
            {
                return null;
            }

            PropertyInfo property = callbacksType.GetProperty(
                RestoringTestContextPropertyName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return IsUsableEventProperty(property) ? property : null;
        }

        internal static bool IsUsableEventProperty(PropertyInfo property)
        {
            return property != null
                && property.CanRead
                && property.CanWrite
                && typeof(Delegate).IsAssignableFrom(property.PropertyType);
        }

        internal static MethodInfo FindBeforeAssemblyReloadMethod(Type runnerType)
        {
            return runnerType.GetMethod(
                BeforeAssemblyReloadMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        /// <summary>
        /// Unsubscribes every run that has finished. A run in progress keeps its subscriptions, and so does a run
        /// cancelled midway, whose runner the Test Framework never destroys, until the next Domain Reload. Call on
        /// the main thread, because it asks Unity which objects are destroyed.
        /// </summary>
        public void Release()
        {
            if (_restoringTestContext == null)
            {
                return;
            }

            Delegate handlers = _restoringTestContext.GetValue(null) as Delegate;
            if (handlers == null)
            {
                return;
            }

            Delegate remaining = handlers;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                if (TryUnsubscribeFinishedRun(handler))
                {
                    remaining = Delegate.Remove(remaining, handler);
                }
            }

            if (remaining != handlers)
            {
                _restoringTestContext.SetValue(null, remaining);
            }
        }

        // Why both events together: measured on Test Framework 1.3.9, a run still on either event stays
        // reachable, so removing only one frees nothing.
        private bool TryUnsubscribeFinishedRun(Delegate handler)
        {
            if (!IsDestroyedUnityObject(handler.Target))
            {
                return false;
            }

            MethodInfo method = FindBeforeAssemblyReloadMethod(handler.Target.GetType());
            if (method == null)
            {
                return false;
            }

            AssemblyReloadEvents.AssemblyReloadCallback callback =
                Delegate.CreateDelegate(
                    typeof(AssemblyReloadEvents.AssemblyReloadCallback),
                    handler.Target,
                    method,
                    throwOnBindFailure: false) as AssemblyReloadEvents.AssemblyReloadCallback;
            if (callback == null)
            {
                return false;
            }

            _unsubscribeBeforeAssemblyReload(callback);
            return true;
        }

        // Why this comparison: the managed instance of a destroyed Unity object survives, and only Unity's
        // overloaded == reports that it is gone. A finished run's runner is destroyed; one still running or
        // cancelled midway is not.
        private static bool IsDestroyedUnityObject(object target)
        {
            UnityEngine.Object unityObject = target as UnityEngine.Object;
            return !ReferenceEquals(unityObject, null) && unityObject == null;
        }
    }
}
