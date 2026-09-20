using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Lets other tools ask hot reload which added members are active without referencing the hot
    /// reload assembly. Null until hot reload initializes.
    /// </summary>
    public static class HotReloadAddedMemberCoordination
    {
        /// <summary>
        /// Set by the hot-reload composition root. Returns the bare name of every member hot reload
        /// currently serves as an addition, empty when none. The names are spelled the way a
        /// compiler diagnostic quotes them: no declaring type, no parameters, and an added property
        /// is listed under the property name as well as under its accessor names.
        /// </summary>
        public static Func<IReadOnlyList<string>> DescribeActiveAddedMemberNames { get; set; }
    }
}
