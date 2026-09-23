using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How a Warnings line of the apply response goes away, which decides whether the Message may
    /// say that one compile clears every warning.
    /// </summary>
    internal enum HotReloadWarningResolution
    {
        // Gone after 'uloop compile': hot reload's own warnings and Skipped methods.
        ClearedByCompile,

        // The caller has to act by hand (re-arm a pause point, wire a field again); a compile
        // does not do it, so one such line rules out the single-compile suffix.
        NeedsCallerAction,

        // Neither: the auto-refresh hold lines, which say nothing about how the rest is cleared.
        NotCounted
    }

    /// <summary>
    /// Collects the Warnings lines of an apply response in order, each with how it is cleared, and
    /// decides from those kinds whether the single-compile resolution suffix applies.
    /// </summary>
    internal sealed class HotReloadResponseWarnings
    {
        // Why two: the suffix says one compile clears "all of them at once", which only adds
        // anything over the warning itself when there is more than one.
        private const int MinimumClearedByCompileForSuffix = 2;

        private readonly List<string> _lines = new List<string>();
        private int _clearedByCompileCount;
        private int _needsCallerActionCount;

        public int Count => _lines.Count;

        // True when two or more lines are cleared by a compile and none needs the caller to act.
        public bool AllowsSingleCompileResolution =>
            _clearedByCompileCount >= MinimumClearedByCompileForSuffix
            && _needsCallerActionCount == 0;

        public void Add(HotReloadWarningResolution resolution, IReadOnlyList<string> lines)
        {
            Debug.Assert(lines != null, "lines must not be null.");

            _lines.AddRange(lines);
            if (resolution == HotReloadWarningResolution.ClearedByCompile)
            {
                _clearedByCompileCount += lines.Count;
                return;
            }

            if (resolution == HotReloadWarningResolution.NeedsCallerAction)
            {
                _needsCallerActionCount += lines.Count;
            }
        }

        public List<string> ToList()
        {
            return new List<string>(_lines);
        }
    }
}
