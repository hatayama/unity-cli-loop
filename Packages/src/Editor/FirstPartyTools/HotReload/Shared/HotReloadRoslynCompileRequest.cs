using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything one Roslyn compilation of hot-reload generated code needs: the sources to write,
    /// where the assembly goes, what it binds against, and whether the AssemblyBuilder fallback is
    /// acceptable for this caller.
    /// </summary>
    internal sealed class HotReloadRoslynCompileRequest
    {
        public IReadOnlyList<HotReloadRoslynCompileSource> Sources { get; }

        public string DllPath { get; }

        public IReadOnlyList<string> ReferencePaths { get; }

        public IReadOnlyList<string> DefineSymbols { get; }

        // Why per-request and not one policy for all hot-reload compiles: a shim may run through
        // the AssemblyBuilder fallback because it is discarded after the reload, while a retained
        // introduced-type artifact must be a real assembly on disk and has to refuse it.
        public bool AllowAssemblyBuilderFallback { get; }

        public HotReloadRoslynCompileRequest(
            IReadOnlyList<HotReloadRoslynCompileSource> sources,
            string dllPath,
            IReadOnlyList<string> referencePaths,
            IReadOnlyList<string> defineSymbols,
            bool allowAssemblyBuilderFallback)
        {
            if (sources == null || sources.Count == 0)
            {
                throw new ArgumentException("Compile sources must not be empty.", nameof(sources));
            }

            if (string.IsNullOrWhiteSpace(dllPath))
            {
                throw new ArgumentException("Dll path must not be empty.", nameof(dllPath));
            }

            // The fallback backend is reached through the single-source entry point only, so a
            // request that allows it and carries several sources could never be honoured as asked.
            if (allowAssemblyBuilderFallback && sources.Count != 1)
            {
                throw new ArgumentException(
                    "The AssemblyBuilder fallback is only available for a single-source compile.",
                    nameof(sources));
            }

            Sources = CopySources(sources);
            DllPath = dllPath;
            ReferencePaths = CopyStrings(referencePaths);
            DefineSymbols = CopyStrings(defineSymbols);
            AllowAssemblyBuilderFallback = allowAssemblyBuilderFallback;
        }

        private static IReadOnlyList<HotReloadRoslynCompileSource> CopySources(
            IReadOnlyList<HotReloadRoslynCompileSource> sources)
        {
            List<HotReloadRoslynCompileSource> copied =
                new List<HotReloadRoslynCompileSource>(sources.Count);
            foreach (HotReloadRoslynCompileSource source in sources)
            {
                if (source == null)
                {
                    throw new ArgumentException("Compile sources must not contain null.", nameof(sources));
                }

                copied.Add(source);
            }

            return copied.AsReadOnly();
        }

        private static IReadOnlyList<string> CopyStrings(IReadOnlyList<string> values)
        {
            return values == null
                ? Array.Empty<string>()
                : new List<string>(values).AsReadOnly();
        }
    }

    /// <summary>
    /// One source file of a compile request: where the compiler writes it and what it writes.
    /// </summary>
    internal sealed class HotReloadRoslynCompileSource
    {
        public string Path { get; }

        public string Text { get; }

        public HotReloadRoslynCompileSource(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Source path must not be empty.", nameof(path));
            }

            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("Source text must not be empty.", nameof(text));
            }

            Path = path;
            Text = text;
        }
    }
}
