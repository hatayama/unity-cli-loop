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
        /// Set by the hot-reload composition root. Returns the metadata name of every active
        /// introduced type, empty when none. These are metadata names as defined in
        /// docs/glossary.md: nested types are joined with '/'. Convert one to the reflection
        /// form before comparing it against Type.FullName or passing it to Assembly.GetType.
        /// </summary>
        public static Func<IReadOnlyList<string>> DescribeActiveTypeNames { get; set; }
    }
}
