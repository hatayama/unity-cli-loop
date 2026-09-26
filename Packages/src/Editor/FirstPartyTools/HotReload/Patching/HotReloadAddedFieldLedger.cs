using System;
using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The added fields one file's generation holds: which fields each type gained, the
    /// initializer text they were last committed with, and what each field declares.
    /// </summary>
    /// <remarks>
    /// Why the three tables live together: a run replaces all of them at once, and a table left
    /// behind would answer for a field the edited source no longer declares. Keeping them in one
    /// object is what makes "replace the added fields" a single move.
    /// </remarks>
    internal sealed class HotReloadAddedFieldLedger
    {
        private readonly Dictionary<string, List<string>> _fieldsByTypeKey =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The initializer text each added field was last committed with, keyed by its display
        // name. Kept beside the type map because it answers a question about the previous
        // reload, not about which fields a type currently holds.
        private readonly Dictionary<string, string> _initializerByFullName =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Every added field the worker described, keyed the way a caller arrives at it: the
        // declaring type in reflection form, a '.', then the field name.
        private readonly Dictionary<string, HotReloadAddedFieldDeclaration> _declarationsByLookupKey =
            new Dictionary<string, HotReloadAddedFieldDeclaration>(StringComparer.Ordinal);

        // The added fields whose declaration carries a serialization attribute. Kept apart from the declarations because those rows are the public
        // wiring contract, and whether Unity would have serialized a field is only ours to report.
        private readonly List<HotReloadSerializedAddedField> _serializedFields =
            new List<HotReloadSerializedAddedField>();

        internal void Clear()
        {
            _fieldsByTypeKey.Clear();
            _initializerByFullName.Clear();
            _declarationsByLookupKey.Clear();
            _serializedFields.Clear();
        }

        /// <summary>
        /// Replaces every entry with <paramref name="addedFieldFullNames"/> (Type.field display
        /// names). Type keys are stored in reflection form (nested types use '+').
        /// <paramref name="addedFieldInitializers"/> holds the initializer text of each name in
        /// the same order, and is ignored when it does not line up with the names.
        /// <paramref name="addedFieldDeclarations"/> carries the store key, declared type and
        /// staticness of each field; a run that reports none leaves no field a caller can wire
        /// from outside a shim. <paramref name="serializedFields"/> names the fields
        /// declared with a serialization attribute; null means none.
        /// </summary>
        internal void Replace(
            IReadOnlyList<string> addedFieldFullNames,
            IReadOnlyList<string> addedFieldInitializers,
            IReadOnlyList<HotReloadAddedFieldDeclaration> addedFieldDeclarations,
            IReadOnlyList<HotReloadSerializedAddedField> serializedFields)
        {
            Debug.Assert(addedFieldFullNames != null, "addedFieldFullNames must not be null.");

            _fieldsByTypeKey.Clear();
            _initializerByFullName.Clear();
            ReplaceDeclarations(addedFieldDeclarations);
            _serializedFields.Clear();
            if (serializedFields != null)
            {
                _serializedFields.AddRange(serializedFields);
            }

            bool hasInitializers =
                addedFieldInitializers != null
                && addedFieldInitializers.Count == addedFieldFullNames.Count;
            for (int index = 0; index < addedFieldFullNames.Count; index++)
            {
                AddField(addedFieldFullNames[index]);
                if (hasInitializers && !string.IsNullOrEmpty(addedFieldFullNames[index]))
                {
                    _initializerByFullName[addedFieldFullNames[index]] =
                        addedFieldInitializers[index] ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Adds to <paramref name="changedFullNames"/> each name of
        /// <paramref name="addedFieldFullNames"/> this ledger already holds an initializer for
        /// that differs from <paramref name="addedFieldInitializers"/>.
        /// </summary>
        /// <remarks>
        /// Why it has to run before the generation starts: beginning an added-member generation
        /// clears the ledger, and the previous reload's initializers go with it.
        /// </remarks>
        internal void CollectFieldsWithChangedInitializer(
            IReadOnlyList<string> addedFieldFullNames,
            IReadOnlyList<string> addedFieldInitializers,
            List<string> changedFullNames)
        {
            Debug.Assert(changedFullNames != null, "changedFullNames must not be null.");
            if (addedFieldFullNames == null
                || addedFieldInitializers == null
                || addedFieldInitializers.Count != addedFieldFullNames.Count)
            {
                return;
            }

            for (int index = 0; index < addedFieldFullNames.Count; index++)
            {
                // Why a declaration that lost its initializer is not reported: the run has no
                // initializer to run anywhere, so there is nothing that fails to reach a value.
                if (string.IsNullOrEmpty(addedFieldInitializers[index]))
                {
                    continue;
                }

                if (!_initializerByFullName.TryGetValue(
                        addedFieldFullNames[index],
                        out string committedInitializer))
                {
                    continue;
                }

                if (string.Equals(
                        committedInitializer,
                        addedFieldInitializers[index],
                        StringComparison.Ordinal))
                {
                    continue;
                }

                changedFullNames.Add(addedFieldFullNames[index]);
            }
        }

        /// <summary>
        /// The row describing one added field of <paramref name="typeName"/>, which may be spelled
        /// either way a nested type is spelled.
        /// </summary>
        internal bool TryGetDeclaration(
            string typeName,
            string fieldName,
            out HotReloadAddedFieldDeclaration declaration)
        {
            declaration = null;
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            return _declarationsByLookupKey.TryGetValue(
                FormatDeclarationLookupKey(typeName, fieldName),
                out declaration);
        }

        internal IReadOnlyList<HotReloadSerializedAddedField> SerializedFields => _serializedFields;

        internal bool HasFields => _fieldsByTypeKey.Count > 0;

        internal void CollectFieldsForType(string normalizedTypeName, HashSet<string> fieldNames)
        {
            Debug.Assert(fieldNames != null, "fieldNames must not be null.");
            if (!_fieldsByTypeKey.TryGetValue(normalizedTypeName, out List<string> fields))
            {
                return;
            }

            for (int index = 0; index < fields.Count; index++)
            {
                fieldNames.Add(fields[index]);
            }
        }

        internal void DescribeFields(string normalizedPath, List<HotReloadAddedFieldDescription> descriptions)
        {
            Debug.Assert(descriptions != null, "descriptions must not be null.");
            foreach (KeyValuePair<string, List<string>> typePair in _fieldsByTypeKey)
            {
                for (int index = 0; index < typePair.Value.Count; index++)
                {
                    descriptions.Add(
                        new HotReloadAddedFieldDescription(
                            normalizedPath,
                            typePair.Key,
                            typePair.Value[index]));
                }
            }
        }

        // Why keyed on the reflection spelling: a caller reaches a field through a runtime Type,
        // whose FullName nests with '+', while the store key the worker built nests with '/'. The
        // conversion happens here, once, and the key itself travels unchanged inside the row.
        private void ReplaceDeclarations(IReadOnlyList<HotReloadAddedFieldDeclaration> addedFieldDeclarations)
        {
            _declarationsByLookupKey.Clear();
            if (addedFieldDeclarations == null)
            {
                return;
            }

            for (int index = 0; index < addedFieldDeclarations.Count; index++)
            {
                HotReloadAddedFieldDeclaration declaration = addedFieldDeclarations[index];
                if (declaration == null
                    || string.IsNullOrEmpty(declaration.DeclaringTypeName)
                    || string.IsNullOrEmpty(declaration.FieldName))
                {
                    continue;
                }

                _declarationsByLookupKey[FormatDeclarationLookupKey(
                    declaration.DeclaringTypeName,
                    declaration.FieldName)] = declaration;
            }
        }

        private void AddField(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }

            int lastDot = fullName.LastIndexOf('.');
            Debug.Assert(
                lastDot > 0 && lastDot < fullName.Length - 1,
                "added field display names are Type.field with a type segment.");
            if (lastDot <= 0 || lastDot >= fullName.Length - 1)
            {
                return;
            }

            string typeKey = NormalizeTypeKey(fullName.Substring(0, lastDot));
            string fieldName = fullName.Substring(lastDot + 1);
            if (!_fieldsByTypeKey.TryGetValue(typeKey, out List<string> fields))
            {
                fields = new List<string>();
                _fieldsByTypeKey[typeKey] = fields;
            }

            if (!fields.Contains(fieldName))
            {
                fields.Add(fieldName);
            }
        }

        private static string FormatDeclarationLookupKey(string typeName, string fieldName)
        {
            return NormalizeTypeKey(typeName) + "." + fieldName;
        }

        private static string NormalizeTypeKey(string typeName)
        {
            return typeName.Replace('/', '+');
        }
    }
}
