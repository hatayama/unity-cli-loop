using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Per-file ledger of fields added by hot reload. Pause-point enable uses this to warn
    /// that those fields never appear in CapturedVariables.
    /// </summary>
    public static class HotReloadAddedFieldRegistry
    {
        private static readonly Dictionary<string, Dictionary<string, List<string>>> FieldsByFileAndType =
            new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        static HotReloadAddedFieldRegistry()
        {
            HotReloadPausePointCoordination.GetAddedFieldsForType = GetFieldsForType;
        }

        /// <summary>
        /// Replaces every added-field entry for <paramref name="filePath"/> with
        /// <paramref name="addedFieldFullNames"/> (Type.field display names). An empty
        /// list deactivates that file's ledger. Type keys are stored in reflection form
        /// (nested types use '+').
        /// </summary>
        public static void ReplaceForFile(string filePath, IReadOnlyList<string> addedFieldFullNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");
            Debug.Assert(addedFieldFullNames != null, "addedFieldFullNames must not be null.");

            string normalizedPath = NormalizeFilePath(filePath);
            if (addedFieldFullNames.Count == 0)
            {
                FieldsByFileAndType.Remove(normalizedPath);
                return;
            }

            Dictionary<string, List<string>> typeMap =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string fullName in addedFieldFullNames)
            {
                if (string.IsNullOrEmpty(fullName))
                {
                    continue;
                }

                int lastDot = fullName.LastIndexOf('.');
                Debug.Assert(
                    lastDot > 0 && lastDot < fullName.Length - 1,
                    "added field display names are Type.field with a type segment.");
                if (lastDot <= 0 || lastDot >= fullName.Length - 1)
                {
                    continue;
                }

                string typeKey = NormalizeTypeKey(fullName.Substring(0, lastDot));
                string fieldName = fullName.Substring(lastDot + 1);
                if (!typeMap.TryGetValue(typeKey, out List<string> fields))
                {
                    fields = new List<string>();
                    typeMap[typeKey] = fields;
                }

                if (!fields.Contains(fieldName))
                {
                    fields.Add(fieldName);
                }
            }

            FieldsByFileAndType[normalizedPath] = typeMap;
        }

        /// <summary>
        /// Returns the de-duplicated simple field names added to <paramref name="typeName"/>
        /// across every file. <paramref name="typeName"/> may be reflection form
        /// (<c>Outer+Inner</c>) or Cecil form (<c>Outer/Inner</c>); this method rewrites
        /// '/' to '+' before lookup so callers do not normalize.
        /// </summary>
        public static IReadOnlyList<string> GetFieldsForType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return Array.Empty<string>();
            }

            string normalizedType = NormalizeTypeKey(typeName);
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, List<string>>> filePair in FieldsByFileAndType)
            {
                if (!filePair.Value.TryGetValue(normalizedType, out List<string> fields))
                {
                    continue;
                }

                foreach (string fieldName in fields)
                {
                    unique.Add(fieldName);
                }
            }

            if (unique.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>(unique);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// Drops every file's added-field map. Called from revert-all; domain reload also
        /// drops the tables because they are static.
        /// </summary>
        public static void ClearAll()
        {
            FieldsByFileAndType.Clear();
        }

        private static string NormalizeFilePath(string filePath)
        {
            return filePath.Replace('\\', '/');
        }

        private static string NormalizeTypeKey(string typeName)
        {
            return typeName.Replace('/', '+');
        }
    }
}
