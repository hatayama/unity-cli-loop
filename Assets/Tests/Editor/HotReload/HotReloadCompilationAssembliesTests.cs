using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the per-domain compilation assembly list: one shared memo, dropped by the
    /// Unity events that can change the list.
    /// </summary>
    public sealed class HotReloadCompilationAssembliesTests
    {
        // Why string names: the handlers are private to the production class; a rename fails these tests
        // instead of silently passing them.
        private const string CompilationStartedHandlerName = "OnCompilationStarted";
        private const string ProjectChangedHandlerName = "OnProjectChanged";

        /// <summary>
        /// Verifies that the static constructor subscribed the memo's invalidation to compile start and
        /// project change.
        /// </summary>
        [Test]
        public void StaticConstructor_SubscribesInvalidationToCompilationStartedAndProjectChanged()
        {
            HotReloadCompilationAssemblies.Current();

            Assert.That(
                StaticEventHasHandler(typeof(CompilationPipeline), CompilationStartedHandlerName),
                Is.True,
                "OnCompilationStarted must be subscribed on CompilationPipeline.");
            Assert.That(
                StaticEventHasHandler(typeof(EditorApplication), ProjectChangedHandlerName),
                Is.True,
                "OnProjectChanged must be subscribed on EditorApplication.");
        }

        /// <summary>
        /// Verifies that two calls share one kept list instead of asking Unity each time.
        /// </summary>
        [Test]
        public void Current_CalledTwice_ReturnsSameInstance()
        {
            IReadOnlyList<UnityCompilationAssembly> first = HotReloadCompilationAssemblies.Current();
            IReadOnlyList<UnityCompilationAssembly> second = HotReloadCompilationAssemblies.Current();

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.SameAs(first));
        }

        // Why: CompilationPipeline stores handlers on Delegate fields, but EditorApplication.projectChanged
        // lives on EventWithPerformanceTracker. A Delegate-only scan cannot see it.
        private static bool StaticEventHasHandler(Type eventOwner, string handlerName)
        {
            FieldInfo[] fields = eventOwner.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                object value = fields[index].GetValue(null);
                if (ContainsProductionHandler(value, handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsProductionHandler(object source, string handlerName)
        {
            if (source == null)
            {
                return false;
            }

            Delegate current = source as Delegate;
            if (current != null)
            {
                return InvocationListContains(current, handlerName);
            }

            return EnumeratorContainsHandler(source, handlerName);
        }

        private static bool InvocationListContains(Delegate current, string handlerName)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int listenerIndex = 0; listenerIndex < listeners.Length; listenerIndex++)
            {
                if (IsProductionHandler(listeners[listenerIndex], handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnumeratorContainsHandler(object source, string handlerName)
        {
            string typeName = source.GetType().Name;
            if (typeName.IndexOf("EventWithPerformanceTracker", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            MethodInfo getEnumerator = source.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public);
            if (getEnumerator == null || getEnumerator.GetParameters().Length != 0)
            {
                return false;
            }

            object enumerator = getEnumerator.Invoke(source, null);
            if (enumerator == null)
            {
                return false;
            }

            MethodInfo moveNext = enumerator.GetType().GetMethod("MoveNext");
            PropertyInfo currentProperty = enumerator.GetType().GetProperty("Current");
            if (moveNext == null || currentProperty == null)
            {
                return false;
            }

            while ((bool)moveNext.Invoke(enumerator, null))
            {
                Delegate listener = currentProperty.GetValue(enumerator) as Delegate;
                if (IsProductionHandler(listener, handlerName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProductionHandler(Delegate listener, string handlerName)
        {
            if (listener == null)
            {
                return false;
            }

            MethodInfo listenerMethod = listener.Method;
            return listenerMethod.DeclaringType == typeof(HotReloadCompilationAssemblies)
                && listenerMethod.Name == handlerName;
        }
    }
}
