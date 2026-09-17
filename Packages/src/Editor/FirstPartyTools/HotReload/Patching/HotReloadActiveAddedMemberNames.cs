using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The member names this domain currently serves through hot-reload additions, in the spelling
    /// a compiler diagnostic quotes them with, so a failure that names one can be recognized.
    /// </summary>
    internal static class HotReloadActiveAddedMemberNames
    {
        private static readonly char[] MemberSeparators = { '.', ':' };

        public static HashSet<string> Collect(
            IReadOnlyList<HotReloadAddedMemberInfo> members,
            IReadOnlyList<HotReloadAddedFieldDescription> fields)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            if (members != null)
            {
                foreach (HotReloadAddedMemberInfo member in members)
                {
                    AddMemberName(names, member.MethodKey);
                }
            }

            if (fields == null)
            {
                return names;
            }

            foreach (HotReloadAddedFieldDescription field in fields)
            {
                if (field.FieldName.Length > 0)
                {
                    names.Add(field.FieldName);
                }
            }

            return names;
        }

        // The ledger spells an added member as the display label
        // '<Namespace>.<Type>.<Name>`<arity>(<parameters>)', while a worker key spells the same
        // member as '<Type>::<Name>(<parameters>)'. Both are reduced to the bare name a compiler
        // diagnostic quotes. An added property is recorded as its accessor methods, so the
        // property name is registered as well: a diagnostic about the property quotes that name.
        private static void AddMemberName(HashSet<string> names, string methodKey)
        {
            int parameterStart = methodKey.IndexOf('(');
            string qualified = parameterStart < 0 ? methodKey : methodKey.Substring(0, parameterStart);
            int arityStart = qualified.IndexOf('`');
            if (arityStart >= 0)
            {
                qualified = qualified.Substring(0, arityStart);
            }

            int separatorIndex = qualified.LastIndexOfAny(MemberSeparators);
            string name = separatorIndex < 0 ? qualified : qualified.Substring(separatorIndex + 1);
            if (name.Length == 0)
            {
                return;
            }

            names.Add(name);
            if (name.StartsWith("get_", StringComparison.Ordinal)
                || name.StartsWith("set_", StringComparison.Ordinal))
            {
                names.Add(name.Substring(4));
            }
        }
    }
}
