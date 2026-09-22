using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The names a failed introduced-type compilation may report as missing members because hot
    /// reload added them rather than because they are typos.
    /// </summary>
    /// <remarks>
    /// Why enum members are kept apart: hot reload can add a method or a field, just not where an
    /// introduced type sees it, while it cannot add an enum member at all. The two need different
    /// hints.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeAddedMemberNames
    {
        internal HotReloadIntroducedTypeAddedMemberNames(
            IReadOnlyCollection<string> members,
            IReadOnlyCollection<string> enumMembers)
        {
            Members = members ?? Array.Empty<string>();
            EnumMembers = enumMembers ?? Array.Empty<string>();
        }

        /// <summary>
        /// Members earlier reloads added and members this reload's sources add to an existing type.
        /// </summary>
        internal IReadOnlyCollection<string> Members { get; }

        /// <summary>Members this reload's sources add to a compiled enum.</summary>
        internal IReadOnlyCollection<string> EnumMembers { get; }
    }
}
