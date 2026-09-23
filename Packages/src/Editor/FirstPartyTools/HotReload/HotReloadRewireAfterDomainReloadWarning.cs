using System;
using System.Collections.Generic;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the warning that asks to wire added fields again after a domain reload discarded
    /// the values an earlier apply wired into them.
    /// </summary>
    internal static class HotReloadRewireAfterDomainReloadWarning
    {
        /// <summary>
        /// Appends the warning when this run added fields to a type whose discarded changes it
        /// recovered. Added fields are not in the Play-entry ledger themselves, so a field counts
        /// as active before the reload when its declaring type had a recovered method.
        /// </summary>
        internal static void Append(
            List<string> warnings,
            IReadOnlyList<string> addedFields,
            IReadOnlyList<string> recoveredDropIdentities)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (addedFields == null)
            {
                throw new ArgumentNullException(nameof(addedFields));
            }

            if (recoveredDropIdentities == null)
            {
                throw new ArgumentNullException(nameof(recoveredDropIdentities));
            }

            HashSet<string> recoveredTypes = CollectRecoveredMethodTypes(recoveredDropIdentities);
            if (recoveredTypes.Count == 0)
            {
                return;
            }

            List<string> rewiredFields = new List<string>();
            for (int index = 0; index < addedFields.Count; index++)
            {
                string field = addedFields[index];
                if (recoveredTypes.Contains(TrimLastSegment(field)))
                {
                    rewiredFields.Add(field);
                }
            }

            if (rewiredFields.Count == 0)
            {
                return;
            }

            warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.RewireAfterDomainReloadWarningFormat,
                    string.Join(", ", rewiredFields)));
        }

        // Why method labels only: an introduced-type identity names a type hot reload declared,
        // and an added field always belongs to a compiled type, so it can never match one.
        private static HashSet<string> CollectRecoveredMethodTypes(IReadOnlyList<string> identities)
        {
            HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < identities.Count; index++)
            {
                string identity = identities[index];
                int parameterStart = identity.IndexOf('(');
                if (parameterStart < 0)
                {
                    continue;
                }

                types.Add(TrimLastSegment(identity.Substring(0, parameterStart)));
            }

            return types;
        }

        // Both a method label ("Type.Method") and an added field ("Type.field") end in one
        // member segment after the declaring type's reflection name. Why the doubled dot: a
        // constructor label is "Type..ctor", whose member name starts with a dot of its own.
        private static string TrimLastSegment(string memberName)
        {
            int lastDot = memberName.LastIndexOf('.');
            if (lastDot > 0 && memberName[lastDot - 1] == '.')
            {
                lastDot--;
            }

            return lastDot < 0 ? string.Empty : memberName.Substring(0, lastDot);
        }
    }
}
