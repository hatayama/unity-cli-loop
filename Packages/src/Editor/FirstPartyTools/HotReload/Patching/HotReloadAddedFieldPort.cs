using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Answers added-field questions about one installed domain, so the validated wiring entry
    /// point in ToolContracts can check a value without referencing the hot-reload assembly.
    /// </summary>
    internal sealed class HotReloadAddedFieldPort : IHotReloadAddedFieldPort
    {
        private readonly HotReloadDomain _domain;

        internal HotReloadAddedFieldPort(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            _domain = domain;
        }

        public bool TryGetDeclaration(
            string declaringTypeName,
            string fieldName,
            out HotReloadAddedFieldDeclaration declaration)
        {
            return _domain.TryGetAddedFieldDeclaration(declaringTypeName, fieldName, out declaration);
        }

        public IReadOnlyList<string> GetAddedFieldNames(string declaringTypeName)
        {
            return _domain.GetAddedFieldsForType(declaringTypeName);
        }
    }
}
