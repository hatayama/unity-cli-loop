using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One method's hot-reload patch as the owning file generation holds it: the shim whose call
    /// replaced the body, the added members that body calls, whether Harmony has accepted the patch
    /// yet, the transplant LocalBuilders in shim slot order, and how many instructions the latest
    /// rebuild prepended.
    /// </summary>
    /// <remarks>
    /// Why the transplant state is written before the patch is committed: the transpiler runs
    /// inside Harmony's Patch call and records it there, while success is known only once Patch
    /// returns. The entry therefore starts pending and carries whatever the rebuild recorded into
    /// its active life. Handing the whole entry back on removal lets a failed unpatch put the patch
    /// back exactly as it stood.
    /// </remarks>
    internal sealed class HotReloadActivePatchEntry
    {
        internal HotReloadActivePatchEntry(
            MethodBase method,
            MethodInfo shim,
            IReadOnlyList<HotReloadCalledAddedMember> calledAddedMembers)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(shim != null, "shim must not be null.");

            Method = method;
            Shim = shim;
            CalledAddedMembers = calledAddedMembers ?? Array.Empty<HotReloadCalledAddedMember>();
        }

        internal MethodBase Method { get; }

        internal MethodInfo Shim { get; }

        /// <summary>
        /// The added members the shim's body calls, fixed when the patch opens: the body is compiled
        /// by then, and a later reload that changes the calls opens a new patch.
        /// </summary>
        internal IReadOnlyList<HotReloadCalledAddedMember> CalledAddedMembers { get; }

        /// <summary>False while Harmony has not yet accepted the patch this entry describes.</summary>
        internal bool IsActive { get; private set; }

        /// <summary>Null when the patch recorded no transplant locals.</summary>
        internal IReadOnlyList<LocalBuilder> TransplantLocals { get; private set; }

        // Why a flag rather than a sentinel length: 0 is a real preamble length, so reading an
        // unrecorded entry as 0 would invent a record the patch never had.
        internal bool HasTransplantPreambleLength { get; private set; }

        internal int TransplantPreambleLength { get; private set; }

        internal void Activate()
        {
            IsActive = true;
        }

        internal void RecordTransplantLocals(IReadOnlyList<LocalBuilder> locals)
        {
            TransplantLocals = locals;
        }

        internal void RecordTransplantPreambleLength(int length)
        {
            HasTransplantPreambleLength = true;
            TransplantPreambleLength = length;
        }
    }
}
