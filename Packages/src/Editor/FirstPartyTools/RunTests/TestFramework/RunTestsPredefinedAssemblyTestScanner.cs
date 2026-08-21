#if ULOOP_HAS_TEST_FRAMEWORK
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds NUnit test methods compiled into Unity predefined assemblies.
    /// </summary>
    internal static class RunTestsPredefinedAssemblyTestScanner
    {
        internal static RunTestsPredefinedAssemblyTestFindings Scan()
        {
            Debug.Assert(
                MainThreadSwitcher.IsMainThread,
                "TypeCache.GetMethodsWithAttribute must run on the main thread because TypeCache is a Unity Editor API.");

            List<(string AssemblyName, string TypeFullName, string MethodName)> methods =
                new List<(string AssemblyName, string TypeFullName, string MethodName)>();
            // Why TypeCache instead of Assembly.GetTypes(): GetTypes() throws
            // ReflectionTypeLoadException when any type in the assembly fails to load.
            Collect(TypeCache.GetMethodsWithAttribute<TestAttribute>(), methods);
            Collect(TypeCache.GetMethodsWithAttribute<TestCaseAttribute>(), methods);
            Collect(TypeCache.GetMethodsWithAttribute<UnityTestAttribute>(), methods);
            return Build(methods);
        }

        internal static RunTestsPredefinedAssemblyTestFindings Build(
            IReadOnlyList<(string AssemblyName, string TypeFullName, string MethodName)> methods)
        {
            Debug.Assert(methods != null, "methods must not be null");

            HashSet<(string AssemblyName, string TypeFullName, string MethodName)> unique =
                new HashSet<(string AssemblyName, string TypeFullName, string MethodName)>();
            List<(string AssemblyName, string TypeFullName, string MethodName)> filtered =
                new List<(string AssemblyName, string TypeFullName, string MethodName)>();
            for (int index = 0; index < methods.Count; index++)
            {
                (string assemblyName, string typeFullName, string methodName) = methods[index];
                Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(typeFullName), "typeFullName must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty");

                if (!IsPredefinedAssembly(assemblyName))
                {
                    continue;
                }

                (string AssemblyName, string TypeFullName, string MethodName) key =
                    (assemblyName, typeFullName, methodName);
                if (!unique.Add(key))
                {
                    continue;
                }

                filtered.Add(key);
            }

            if (filtered.Count == 0)
            {
                return RunTestsPredefinedAssemblyTestFindings.None();
            }

            filtered.Sort(CompareMethods);
            int sampleCount = filtered.Count;
            if (sampleCount > RunTestsConstants.PredefinedAssemblyTestSampleLimit)
            {
                sampleCount = RunTestsConstants.PredefinedAssemblyTestSampleLimit;
            }

            string[] sampleEntries = new string[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                (string assemblyName, string typeFullName, string methodName) = filtered[index];
                sampleEntries[index] = assemblyName + ": " + typeFullName + "." + methodName;
            }

            return RunTestsPredefinedAssemblyTestFindings.Create(filtered.Count, sampleEntries);
        }

        private static void Collect(
            TypeCache.MethodCollection methods,
            List<(string AssemblyName, string TypeFullName, string MethodName)> destination)
        {
            Debug.Assert(destination != null, "destination must not be null");

            foreach (MethodInfo method in methods)
            {
                if (method == null || method.DeclaringType == null)
                {
                    continue;
                }

                string assemblyName = method.DeclaringType.Assembly.GetName().Name;
                if (string.IsNullOrEmpty(assemblyName))
                {
                    continue;
                }

                string typeFullName = method.DeclaringType.FullName;
                if (string.IsNullOrEmpty(typeFullName))
                {
                    typeFullName = method.DeclaringType.Name;
                }

                destination.Add((assemblyName, typeFullName, method.Name));
            }
        }

        private static bool IsPredefinedAssembly(string assemblyName)
        {
            return assemblyName == RunTestsConstants.PredefinedAssemblyCSharpName
                   || assemblyName == RunTestsConstants.PredefinedAssemblyCSharpEditorName
                   || assemblyName == RunTestsConstants.PredefinedAssemblyCSharpFirstpassName
                   || assemblyName == RunTestsConstants.PredefinedAssemblyCSharpEditorFirstpassName;
        }

        private static int CompareMethods(
            (string AssemblyName, string TypeFullName, string MethodName) left,
            (string AssemblyName, string TypeFullName, string MethodName) right)
        {
            int assemblyCompare = string.CompareOrdinal(left.AssemblyName, right.AssemblyName);
            if (assemblyCompare != 0)
            {
                return assemblyCompare;
            }

            int typeCompare = string.CompareOrdinal(left.TypeFullName, right.TypeFullName);
            if (typeCompare != 0)
            {
                return typeCompare;
            }

            return string.CompareOrdinal(left.MethodName, right.MethodName);
        }
    }
}
#endif
