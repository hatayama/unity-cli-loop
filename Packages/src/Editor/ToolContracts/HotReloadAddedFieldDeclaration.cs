namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// One added field as the transform worker declared it: the store key its shims read and
    /// write, the declaring type and field name a caller names it by, the declared type a value
    /// has to be assignable to, and whether the field is static.
    /// </summary>
    /// <remarks>
    /// Why the store key travels instead of being rebuilt: the key spells nested types the
    /// metadata way ('/') while <see cref="DeclaringTypeName"/> spells them the reflection way
    /// ('+'), and rebuilding one from the other at every use is what makes a nested type silently
    /// miss. The worker forms the key once and it is carried unchanged from there.
    /// </remarks>
    public sealed class HotReloadAddedFieldDeclaration
    {
        /// <summary>The key the shims pass to the added-field store, nested types with '/'.</summary>
        public string StoreFieldKey { get; }

        /// <summary>The declaring type in reflection form, nested types with '+'.</summary>
        public string DeclaringTypeName { get; }

        public string FieldName { get; }

        /// <summary>
        /// The assembly-qualified name of the field's declared type, as the worker's compilation
        /// saw it. Empty when the worker could not name the type.
        /// </summary>
        public string DeclaredTypeAssemblyQualifiedName { get; }

        public bool IsStatic { get; }

        public HotReloadAddedFieldDeclaration(
            string storeFieldKey,
            string declaringTypeName,
            string fieldName,
            string declaredTypeAssemblyQualifiedName,
            bool isStatic)
        {
            StoreFieldKey = storeFieldKey ?? string.Empty;
            DeclaringTypeName = declaringTypeName ?? string.Empty;
            FieldName = fieldName ?? string.Empty;
            DeclaredTypeAssemblyQualifiedName = declaredTypeAssemblyQualifiedName ?? string.Empty;
            IsStatic = isStatic;
        }
    }
}
