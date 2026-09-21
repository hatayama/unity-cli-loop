using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns the transform worker's added-field declaration rows into the ledger rows the Editor
    /// keeps, converting the declaring type name out of metadata form exactly here.
    /// </summary>
    /// <remarks>
    /// Why the conversion lives at this one call: the worker spells a nested declaring type
    /// Outer/Inner and every Editor-side lookup compares against Type.FullName, which spells it
    /// Outer+Inner. Converting at the boundary is what keeps a nested type from silently failing
    /// to match wherever the row is read.
    /// </remarks>
    internal static class HotReloadAddedFieldDeclarationConversion
    {
        internal static HotReloadAddedFieldDeclaration[] FromWorkerRows(
            TransformWorkerAddedFieldDeclarationDto[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                return Array.Empty<HotReloadAddedFieldDeclaration>();
            }

            List<HotReloadAddedFieldDeclaration> declarations =
                new List<HotReloadAddedFieldDeclaration>(rows.Length);
            foreach (TransformWorkerAddedFieldDeclarationDto row in rows)
            {
                // A row without a declaring type or field name names no field, so nothing could
                // look it up. Dropping it here keeps the ledger free of entries that can only
                // ever miss.
                if (row == null
                    || string.IsNullOrEmpty(row.declaringTypeMetadataName)
                    || string.IsNullOrEmpty(row.fieldName))
                {
                    continue;
                }

                declarations.Add(new HotReloadAddedFieldDeclaration(
                    row.fieldKey,
                    new HotReloadMetadataTypeName(row.declaringTypeMetadataName).ToReflectionName().Value,
                    row.fieldName,
                    row.declaredTypeAssemblyQualifiedName,
                    row.isStatic));
            }

            return declarations.ToArray();
        }

        /// <summary>
        /// The Type.field names, spelled as C# source spells them, of the rows whose declaration
        /// carries a serialization attribute.
        /// </summary>
        /// <remarks>
        /// Why the source spelling rather than the reflection one: these names only reach a
        /// reader, who wrote the type as Outer.Inner, and nothing looks a field up by them.
        /// </remarks>
        internal static string[] ListSerializedFieldDisplayNames(
            TransformWorkerAddedFieldDeclarationDto[] rows)
        {
            if (rows == null || rows.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>();
            foreach (TransformWorkerAddedFieldDeclarationDto row in rows)
            {
                if (row == null
                    || !row.hasSerializationAttribute
                    || string.IsNullOrEmpty(row.declaringTypeMetadataName)
                    || string.IsNullOrEmpty(row.fieldName))
                {
                    continue;
                }

                names.Add(
                    new HotReloadMetadataTypeName(row.declaringTypeMetadataName).ToDisplayShortName()
                    + "." + row.fieldName);
            }

            return names.ToArray();
        }
    }
}
