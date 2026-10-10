using System;
using System.Collections.Generic;

using Mono.Cecil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Groups one compiled assembly's call sites by the method they reference, keyed by the
    /// open declaring type's full name and the method name. Why: a caller scan looks for a few
    /// methods, and comparing each of them with every call site of a large dll cost seconds per
    /// run; the scan now compares only the call sites filed under its targets' keys.
    /// </summary>
    internal sealed class HotReloadCompiledCallSiteIndex
    {
        private readonly Dictionary<string, List<int>> _positionsByKey;

        private HotReloadCompiledCallSiteIndex(Dictionary<string, List<int>> positionsByKey)
        {
            _positionsByKey = positionsByKey;
        }

        /// <summary>
        /// Files every call site under its operand's key; each key's positions ascend.
        /// </summary>
        internal static HotReloadCompiledCallSiteIndex Build(
            List<HotReloadCompiledCallSiteCache.CompiledCallSite> callSites)
        {
            Dictionary<string, List<int>> positionsByKey = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int position = 0; position < callSites.Count; position++)
            {
                MethodReference openMethod = callSites[position].Operand.GetElementMethod();
                // Why leave it out: a scan never matches an operand without a declaring type.
                if (openMethod.DeclaringType == null)
                {
                    continue;
                }

                string key = BuildKey(GetOpenDeclaringType(openMethod.DeclaringType).FullName, openMethod.Name);
                if (!positionsByKey.TryGetValue(key, out List<int> positions))
                {
                    positions = new List<int>();
                    positionsByKey.Add(key, positions);
                }

                positions.Add(position);
            }

            return new HotReloadCompiledCallSiteIndex(positionsByKey);
        }

        /// <summary>
        /// Ascending positions of the call sites filed under the type and method name; empty when none.
        /// </summary>
        internal IReadOnlyList<int> Lookup(string openDeclaringTypeFullName, string methodName)
        {
            if (_positionsByKey.TryGetValue(BuildKey(openDeclaringTypeFullName, methodName), out List<int> positions))
            {
                return positions;
            }

            return Array.Empty<int>();
        }

        /// <summary>
        /// The generic type definition behind a constructed declaring type, so a call through
        /// Host&lt;int&gt; is filed and matched under Host`1. The scan's identity match uses this
        /// too, so the key and the match open a type the same way.
        /// </summary>
        internal static TypeReference GetOpenDeclaringType(TypeReference declaringType)
        {
            GenericInstanceType genericInstance = declaringType as GenericInstanceType;
            if (genericInstance != null)
            {
                return genericInstance.GetElementType();
            }

            return declaringType;
        }

        private static string BuildKey(string openDeclaringTypeFullName, string methodName)
        {
            return openDeclaringTypeFullName + "::" + methodName;
        }
    }
}
