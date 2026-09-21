using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// What the validated added-field entry point may ask hot reload about the added fields of the
    /// domain it currently has installed. Implemented by hot reload, read through
    /// <see cref="HotReloadAddedFieldCoordination.ActiveFields"/>.
    /// </summary>
    public interface IHotReloadAddedFieldPort
    {
        /// <summary>
        /// The row describing one added field. <paramref name="declaringTypeName"/> is a type full
        /// name in either nested spelling (<c>Outer+Inner</c> or <c>Outer/Inner</c>); false when
        /// no active reload added that field to that exact type.
        /// </summary>
        bool TryGetDeclaration(
            string declaringTypeName,
            string fieldName,
            out HotReloadAddedFieldDeclaration declaration);

        /// <summary>
        /// The simple names of every field an active reload added to <paramref name="declaringTypeName"/>
        /// itself, empty when none. Used to tell a caller what it could have meant.
        /// </summary>
        IReadOnlyList<string> GetAddedFieldNames(string declaringTypeName);
    }
}
