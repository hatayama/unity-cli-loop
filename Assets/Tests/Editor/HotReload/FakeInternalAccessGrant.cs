using System;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// An internal access grant whose availability and outcome a test decides, so the compiler
    /// and the preparation can be driven through every grant outcome without writing to the
    /// runtime's method records.
    /// </summary>
    /// <remarks>
    /// Why granting changes nothing: a test that needs the artifact to actually reach internal
    /// members uses the production grant, and every other test only needs to see whether and on
    /// which assembly the grant was asked for.
    /// </remarks>
    internal sealed class FakeInternalAccessGrant : IHotReloadInternalAccessGrant
    {
        internal const string UnavailableTestReason = "Internal access grants are unavailable in this test.";

        internal FakeInternalAccessGrant(bool isAvailable)
        {
            IsAvailable = isAvailable;
            UnavailableReason = isAvailable ? string.Empty : UnavailableTestReason;
        }

        public bool IsAvailable { get; }

        public string UnavailableReason { get; }

        /// <summary>
        /// The reason the next grants are refused with, or null to grant them. Settable so a test
        /// can let an earlier reload succeed and refuse only a later one.
        /// </summary>
        internal string RefusalReason { get; set; }

        internal int GrantCalls { get; private set; }

        internal Assembly LastGrantedAssembly { get; private set; }

        // Why the call is counted before the availability check: a caller that asks an
        // unavailable grant is exactly what a test has to be able to see.
        public HotReloadInternalAccessGrantResult Grant(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            GrantCalls++;
            if (!IsAvailable)
            {
                throw new InvalidOperationException(UnavailableReason);
            }

            LastGrantedAssembly = assembly;
            return RefusalReason == null
                ? HotReloadInternalAccessGrantResult.Granted(0)
                : HotReloadInternalAccessGrantResult.Refused(RefusalReason);
        }
    }
}
