using System;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads an added instance field's slot through <see cref="HotReloadAddedFieldValues.TryGet"/>,
    /// which restores a wired value on a miss and never runs the field's initializer.
    /// </summary>
    internal sealed class HotReloadAddedFieldSlotReader : IHotReloadWiredValueSlotReader
    {
        private readonly IHotReloadAddedFieldPort _declarations;
        private readonly HotReloadAddedFieldValues _values;

        internal HotReloadAddedFieldSlotReader(IHotReloadAddedFieldPort declarations, HotReloadAddedFieldValues values)
        {
            Debug.Assert(declarations != null, "declarations must not be null.");
            Debug.Assert(values != null, "values must not be null.");
            _declarations = declarations;
            _values = values;
        }

        public bool TryRead(object host, string storeFieldKey)
        {
            Debug.Assert(host != null, "host must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(storeFieldKey), "storeFieldKey must not be empty.");

            int separator = storeFieldKey.LastIndexOf(HotReloadAddedFieldStore.FieldKeySeparator, StringComparison.Ordinal);
            if (separator < 0)
            {
                return false;
            }

            string fieldName = storeFieldKey.Substring(separator + HotReloadAddedFieldStore.FieldKeySeparator.Length);
            // The base types too: an added field may be declared on a base of the host's type.
            for (Type current = host.GetType(); current != null; current = current.BaseType)
            {
                if (!_declarations.TryGetDeclaration(current.FullName, fieldName, out HotReloadAddedFieldDeclaration declaration))
                {
                    continue;
                }

                // Exact key: the same field name on another declaring type is another slot.
                if (declaration.IsStatic
                    || !string.Equals(declaration.StoreFieldKey, storeFieldKey, StringComparison.Ordinal))
                {
                    return false;
                }

                Type fieldType = Type.GetType(declaration.DeclaredTypeAssemblyQualifiedName, false);
                if (fieldType == null)
                {
                    return false;
                }

                // TryGet, not GetOrInit: a miss must not run the initializer or leave a pending slot.
                return _values.TryGet(host, storeFieldKey, fieldType, out _);
            }

            return false;
        }
    }
}
