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

        /// <summary>
        /// Verifies that the static constructor subscribed the memo's invalidation to compile start but not
        /// to project change, because Unity raises project change for the import that triggered a compile
        /// after the new domain's startup capture has already filled the memo.
        /// </summary>
        [Test]
        public void StaticConstructor_SubscribesInvalidationToCompilationStartedOnly()
        {
            HotReloadCompilationAssemblies.Current();

            Assert.That(
                StaticEventHasHandler(
                    typeof(CompilationPipeline),
                    listener => IsNamedProductionHandler(listener, CompilationStartedHandlerName)),
                Is.True,
                "OnCompilationStarted must be subscribed on CompilationPipeline.");
            Assert.That(
                StaticEventHasHandler(typeof(EditorApplication), IsAnyProductionHandler),
                Is.False,
                "Nothing of HotReloadCompilationAssemblies or its cache may be subscribed on EditorApplication: projectChanged fires after the startup capture of every compile.");
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
        private static bool StaticEventHasHandler(Type eventOwner, Func<Delegate, bool> isMatch)
        {
            FieldInfo[] fields = eventOwner.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                object value = fields[index].GetValue(null);
                if (ContainsHandler(value, isMatch))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsHandler(object source, Func<Delegate, bool> isMatch)
        {
            if (source == null)
            {
                return false;
            }

            Delegate current = source as Delegate;
            if (current != null)
            {
                return InvocationListContains(current, isMatch);
            }

            return EnumeratorContainsHandler(source, isMatch);
        }

        private static bool InvocationListContains(Delegate current, Func<Delegate, bool> isMatch)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int listenerIndex = 0; listenerIndex < listeners.Length; listenerIndex++)
            {
                if (isMatch(listeners[listenerIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EnumeratorContainsHandler(object source, Func<Delegate, bool> isMatch)
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
                if (listener != null && isMatch(listener))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNamedProductionHandler(Delegate listener, string handlerName)
        {
            MethodInfo listenerMethod = listener.Method;
            return listenerMethod.DeclaringType == typeof(HotReloadCompilationAssemblies)
                && listenerMethod.Name == handlerName;
        }

        // Why walk the declaring types outward: a lambda subscribed from the production class lives on a
        // compiler-generated type nested in it, so a name or direct-type match would miss it.
        // Why also the cache type: subscribing the memo's own Invalidate as a method group puts the
        // handler on HotReloadCompilationAssemblyCache, a top-level type.
        private static bool IsAnyProductionHandler(Delegate listener)
        {
            for (Type declaringType = listener.Method.DeclaringType;
                declaringType != null;
                declaringType = declaringType.DeclaringType)
            {
                if (declaringType == typeof(HotReloadCompilationAssemblies)
                    || declaringType == typeof(HotReloadCompilationAssemblyCache))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
