using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Lets other tools ask hot reload which introduced types are active without referencing
    /// the hot reload assembly. Null until hot reload initializes.
    /// </summary>
    public static class HotReloadIntroducedTypeCoordination
    {
        /// <summary>
        /// Set by HotReloadIntroducedTypeHolder. Returns the metadata names
        /// (Namespace.Outer+Nested form) of every active introduced type, empty when none.
        /// </summary>
        public static Func<IReadOnlyList<string>> DescribeActiveTypeNames { get; set; }
    }
}
