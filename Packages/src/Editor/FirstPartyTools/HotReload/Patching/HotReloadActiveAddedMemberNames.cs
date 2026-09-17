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
        private const string MethodKeySeparator = "::";

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

        // A method key spells a member as '<Type>::<Name>(<parameters>)'. An added property is
        // recorded as its accessor methods, so the property name is registered as well: a member
        // the source reaches through the property is quoted by the compiler under that name.
        private static void AddMemberName(HashSet<string> names, string methodKey)
        {
            int separatorIndex = methodKey.IndexOf(MethodKeySeparator, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return;
            }

            int nameStart = separatorIndex + MethodKeySeparator.Length;
            int parameterStart = methodKey.IndexOf('(', nameStart);
            string name = parameterStart < 0
                ? methodKey.Substring(nameStart)
                : methodKey.Substring(nameStart, parameterStart - nameStart);
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
