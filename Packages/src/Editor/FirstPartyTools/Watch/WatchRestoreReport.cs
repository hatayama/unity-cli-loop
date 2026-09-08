using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Result of re-registering the stored watch expressions after a domain reload.
    /// </summary>
    internal sealed class WatchRestoreReport
    {
        public static readonly WatchRestoreReport Empty =
            new(0, Array.Empty<string>());

        public WatchRestoreReport(int restoredCount, IReadOnlyList<string> warnings)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            RestoredCount = restoredCount;
            Warnings = warnings;
        }

        public int RestoredCount { get; }

        /// <summary>Watches that were dropped, one human-readable line each.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
