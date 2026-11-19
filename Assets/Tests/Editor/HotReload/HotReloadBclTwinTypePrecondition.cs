using System;
using System.Reflection;

using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Guards the precondition of the BCL twin type tests: they exercise the worker's binding only
    /// while a referenced assembly carries internal copies of BCL types. Today that is Harmony, which
    /// internalizes ReadOnlySpan and Memory. Without this guard a Harmony update that dropped the
    /// copies would leave the tests passing with the fix removed.
    /// </summary>
    internal static class HotReloadBclTwinTypePrecondition
    {
        private const string TwinCarrierAssemblyName = "UnityCliLoop.0Harmony";

        internal static void AssertReferencedAssemblyCarriesInternalBclTwins()
        {
            Assembly carrier = FindLoadedAssemblyOrNull(TwinCarrierAssemblyName);
            Assert.That(carrier, Is.Not.Null, TwinCarrierAssemblyName + " is not loaded. " + MoveMessage());
            AssertInternalCopy(carrier, "System.ReadOnlySpan`1");
            AssertInternalCopy(carrier, "System.Memory`1");
        }

        private static void AssertInternalCopy(Assembly carrier, string typeName)
        {
            Type copy = carrier.GetType(typeName, throwOnError: false);
            Assert.That(copy, Is.Not.Null, TwinCarrierAssemblyName + " no longer declares " + typeName + ". " + MoveMessage());
            Assert.That(copy.IsPublic, Is.False, TwinCarrierAssemblyName + " declares " + typeName + " as public. " + MoveMessage());
        }

        private static Assembly FindLoadedAssemblyOrNull(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == name)
                {
                    return assembly;
                }
            }

            return null;
        }

        private static string MoveMessage()
        {
            return "The BCL twin type tests only reproduce the binding they cover while a referenced assembly "
                + "carries an internal copy of a BCL type; move them to another reference that does.";
        }
    }
}
