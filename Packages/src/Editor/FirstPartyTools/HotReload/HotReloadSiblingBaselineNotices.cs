using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Collects the missing-baseline notices of the files a run pulled back in only to re-apply
    /// them, and reports them as one warning per kind instead of one per file.
    /// </summary>
    /// <remarks>
    /// Why not one warning per file like the files the caller passed: a re-applied file was not
    /// edited, so its warning came back on every reload until the next compile and outnumbered
    /// the warnings about the files the caller did edit.
    /// </remarks>
    internal sealed class HotReloadSiblingBaselineNotices
    {
        // Indexed by HotReloadMissingBaselineKind; the order is also the order of the warnings.
        private static readonly string[] WarningFormatByKind =
        {
            HotReloadConstants.SiblingIntroducedTypeNoBaselineWarningFormat,
            HotReloadConstants.SiblingNoVerifiedSourceSnapshotWarningFormat,
            HotReloadConstants.SiblingNoCompiledMethodBodyBaselineWarningFormat,
        };

        // Why a set: one path spelled once per kind keeps the count equal to the files listed.
        private readonly SortedSet<string>[] _pathsByKind =
        {
            new SortedSet<string>(StringComparer.Ordinal),
            new SortedSet<string>(StringComparer.Ordinal),
            new SortedSet<string>(StringComparer.Ordinal),
        };

        internal void Add(HotReloadMissingBaselineKind kind, string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            _pathsByKind[(int)kind].Add(projectRelativePath);
        }

        internal void AppendTo(List<string> warnings)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            for (int kind = 0; kind < _pathsByKind.Length; kind++)
            {
                SortedSet<string> paths = _pathsByKind[kind];
                if (paths.Count == 0)
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        WarningFormatByKind[kind],
                        paths.Count,
                        string.Join(", ", paths)));
            }
        }
    }
}
