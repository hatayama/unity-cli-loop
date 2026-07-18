using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the Unity compiler settings required by the asynchronous Roslyn pipeline.
    /// </summary>
    public sealed class RoslynCompilerOptions
    {
        public IReadOnlyList<string> DefineSymbols { get; }

        public bool AllowUnsafeCode { get; }

        /// <summary>
        /// Creates an immutable compiler-settings snapshot for one compilation request.
        /// </summary>
        public RoslynCompilerOptions(
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode)
        {
            Debug.Assert(defineSymbols != null, "defineSymbols must not be null");

            List<string> filteredDefineSymbols = new(defineSymbols.Count);
            foreach (string defineSymbol in defineSymbols)
            {
                if (!string.IsNullOrWhiteSpace(defineSymbol))
                {
                    filteredDefineSymbols.Add(defineSymbol);
                }
            }

            DefineSymbols = filteredDefineSymbols.AsReadOnly();
            AllowUnsafeCode = allowUnsafeCode;
        }
    }
}
