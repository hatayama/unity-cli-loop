using System;
using System.Diagnostics;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure reflection lookup for TestRunnerApi cancel helpers. Separated so unit tests can
    /// exercise TF version differences without Unity assemblies.
    /// </summary>
    internal static class TestRunnerApiCancelMethodLookup
    {
        /// <summary>
        /// Resolves CancelTestRun(string) and parameterless IsRunActive() on the given type.
        /// </summary>
        public static (MethodInfo CancelTestRun, MethodInfo IsRunActive, string Log) Resolve(Type testRunnerApiType)
        {
            Debug.Assert(testRunnerApiType != null, "testRunnerApiType must not be null");

            // Why Public|NonPublic: TF 1.3.9 keeps CancelTestRun internal; TF 1.4+ (Unity 6000)
            // publishes the same CancelTestRun(string) signature.
            const BindingFlags flags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            MethodInfo cancelTestRun = testRunnerApiType.GetMethod(
                "CancelTestRun",
                flags,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            // Why parameterless only: TF 2.0 changes IsRunActive to IsRunActive(string guid).
            // Missing parameterless method is expected there; fall back to Option B polling.
            MethodInfo isRunActive = testRunnerApiType.GetMethod(
                "IsRunActive",
                flags,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            string log = null;
            if (cancelTestRun == null)
            {
                log =
                    "TestRunnerApi.CancelTestRun(string) was not found; " +
                    "run-tests cancel will use the public Play Mode exit fallback.";
            }
            else if (isRunActive == null)
            {
                log =
                    "TestRunnerApi.IsRunActive() was not found; " +
                    "EditMode cancel will skip active-run polling and rely on CancelTestRun / Play Mode exit.";
            }

            return (cancelTestRun, isRunActive, log);
        }
    }
}
