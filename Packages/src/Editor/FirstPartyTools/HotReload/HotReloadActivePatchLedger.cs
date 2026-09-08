using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records the live hot-reload patch of each method (shim, source file, transplant locals,
    /// transplant preamble length) so apply and revert can add, drop and restore one method's
    /// state as a unit.
    /// </summary>
    // Why the transplant tables are written before the shim: the transpiler runs inside
    // Harmony's Patch call and records them there, while the shim and source path are known only
    // once Patch returns. Recording and activating are therefore separate operations.
    internal sealed class HotReloadActivePatchLedger
    {
        private readonly Dictionary<MethodBase, MethodInfo> _shimByMethod =
            new Dictionary<MethodBase, MethodInfo>();

        private readonly Dictionary<MethodBase, string> _filePathByMethod =
            new Dictionary<MethodBase, string>();

        private readonly Dictionary<MethodBase, IReadOnlyList<LocalBuilder>> _transplantLocalsByMethod =
            new Dictionary<MethodBase, IReadOnlyList<LocalBuilder>>();

        private readonly Dictionary<MethodBase, int> _transplantPreambleLengthByMethod =
            new Dictionary<MethodBase, int>();

        /// <summary>How many methods have an active patch recorded.</summary>
        internal int Count => _shimByMethod.Count;

        /// <summary>The recorded method and source path of every active patch.</summary>
        internal IEnumerable<KeyValuePair<MethodBase, string>> FilePathEntries => _filePathByMethod;

        /// <summary>The recorded method and shim of every active patch.</summary>
        internal IEnumerable<KeyValuePair<MethodBase, MethodInfo>> ShimEntries => _shimByMethod;

        /// <summary>Whether a patch is recorded for this method.</summary>
        internal bool IsActive(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _shimByMethod.ContainsKey(method);
        }

        /// <summary>The shim that replaced this method, or null when no patch is recorded.</summary>
        internal MethodInfo FindShim(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _shimByMethod.TryGetValue(method, out MethodInfo shim) ? shim : null;
        }

        /// <summary>The source path recorded with this method's patch, or empty when none is.</summary>
        internal string FindFilePath(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _filePathByMethod.TryGetValue(method, out string filePath) ? filePath : string.Empty;
        }

        /// <summary>
        /// The shim-slot locals of this method's latest transplant rebuild, or null when none was
        /// recorded.
        /// </summary>
        internal IReadOnlyList<LocalBuilder> FindTransplantLocals(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _transplantLocalsByMethod.TryGetValue(method, out IReadOnlyList<LocalBuilder> locals)
                ? locals
                : null;
        }

        /// <summary>
        /// How many preamble instructions this method's latest transplant rebuild inserted, or 0
        /// when none was recorded.
        /// </summary>
        internal int FindTransplantPreambleLength(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _transplantPreambleLengthByMethod.TryGetValue(method, out int length) ? length : 0;
        }

        /// <summary>The methods that currently have an active patch.</summary>
        internal List<MethodBase> ListMethods()
        {
            return new List<MethodBase>(_shimByMethod.Keys);
        }

        /// <summary>Records the shim-slot locals a transplant rebuild produced for this method.</summary>
        internal void RecordTransplantLocals(MethodBase method, IReadOnlyList<LocalBuilder> locals)
        {
            Debug.Assert(method != null, "method must not be null.");
            _transplantLocalsByMethod[method] = locals;
        }

        /// <summary>Records the preamble length a transplant rebuild produced for this method.</summary>
        internal void RecordTransplantPreambleLength(MethodBase method, int length)
        {
            Debug.Assert(method != null, "method must not be null.");
            _transplantPreambleLengthByMethod[method] = length;
        }

        /// <summary>
        /// Marks this method as patched by the given shim. Any transplant state the rebuild
        /// already recorded belongs to this same patch and is kept.
        /// </summary>
        internal void Activate(MethodBase method, MethodInfo shim, string filePath)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(shim != null, "shim must not be null.");
            Debug.Assert(filePath != null, "filePath must not be null.");
            _shimByMethod[method] = shim;
            _filePathByMethod[method] = filePath;
        }

        /// <summary>
        /// Drops everything recorded for this method and hands the removed state back, so a caller
        /// whose unpatch fails can put the entry back whole. Returns null when no patch was
        /// recorded — the transplant state is dropped either way, because a failed apply can leave
        /// it behind with no shim ever registered.
        /// </summary>
        internal HotReloadActivePatchEntry Remove(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            bool hasLocals = _transplantLocalsByMethod.TryGetValue(
                method, out IReadOnlyList<LocalBuilder> locals);
            bool hasPreambleLength = _transplantPreambleLengthByMethod.TryGetValue(
                method, out int preambleLength);
            _transplantLocalsByMethod.Remove(method);
            _transplantPreambleLengthByMethod.Remove(method);

            if (!_shimByMethod.TryGetValue(method, out MethodInfo shim))
            {
                _filePathByMethod.Remove(method);
                return null;
            }

            string filePath = _filePathByMethod.TryGetValue(method, out string recordedPath)
                ? recordedPath
                : null;
            _shimByMethod.Remove(method);
            _filePathByMethod.Remove(method);
            return new HotReloadActivePatchEntry(
                shim,
                filePath,
                hasLocals ? locals : null,
                hasPreambleLength,
                preambleLength);
        }

        /// <summary>Puts back an entry a previous Remove handed out. A null entry restores nothing.</summary>
        internal void Restore(MethodBase method, HotReloadActivePatchEntry entry)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (entry == null)
            {
                return;
            }

            _shimByMethod[method] = entry.Shim;
            _filePathByMethod[method] = entry.FilePath ?? string.Empty;
            if (entry.TransplantLocals != null)
            {
                _transplantLocalsByMethod[method] = entry.TransplantLocals;
            }

            if (entry.HasTransplantPreambleLength)
            {
                _transplantPreambleLengthByMethod[method] = entry.TransplantPreambleLength;
            }
        }

        /// <summary>Drops every recorded patch.</summary>
        internal void Clear()
        {
            _shimByMethod.Clear();
            _filePathByMethod.Clear();
            _transplantLocalsByMethod.Clear();
            _transplantPreambleLengthByMethod.Clear();
        }
    }

    /// <summary>
    /// One method's patch state as it stood in the ledger, handed back by Remove so a failed
    /// revert can put it back whole.
    /// </summary>
    internal sealed class HotReloadActivePatchEntry
    {
        internal HotReloadActivePatchEntry(
            MethodInfo shim,
            string filePath,
            IReadOnlyList<LocalBuilder> transplantLocals,
            bool hasTransplantPreambleLength,
            int transplantPreambleLength)
        {
            Shim = shim;
            FilePath = filePath;
            TransplantLocals = transplantLocals;
            HasTransplantPreambleLength = hasTransplantPreambleLength;
            TransplantPreambleLength = transplantPreambleLength;
        }

        internal MethodInfo Shim { get; }

        internal string FilePath { get; }

        /// <summary>Null when the patch recorded no transplant locals.</summary>
        internal IReadOnlyList<LocalBuilder> TransplantLocals { get; }

        // Why a flag rather than a sentinel length: 0 is a real preamble length, so restoring an
        // unrecorded entry as 0 would invent a record the patch never had.
        internal bool HasTransplantPreambleLength { get; }

        internal int TransplantPreambleLength { get; }
    }
}
