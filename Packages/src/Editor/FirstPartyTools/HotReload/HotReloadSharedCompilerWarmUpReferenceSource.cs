using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Lists the compile references the next shim compile would bind as they are, for the most
    /// recently edited assembly that is compiled, so the shared compiler worker can load them
    /// before the first run after a server reset.
    /// </summary>
    internal sealed class HotReloadSharedCompilerWarmUpReferenceSource
    {
        private readonly string _projectRoot;
        private readonly Func<string, IReadOnlyList<string>> _readLedger;
        private readonly Func<string, UnityCompilationAssembly> _findCompilationAssembly;

        internal HotReloadSharedCompilerWarmUpReferenceSource(
            string projectRoot,
            Func<string, IReadOnlyList<string>> readLedger,
            Func<string, UnityCompilationAssembly> findCompilationAssembly)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(readLedger != null, "readLedger must not be null.");
            Debug.Assert(findCompilationAssembly != null, "findCompilationAssembly must not be null.");
            _projectRoot = projectRoot;
            _readLedger = readLedger;
            _findCompilationAssembly = findCompilationAssembly;
        }

        /// <summary>
        /// Any thread. Empty when the ledger names no assembly whose dll exists and whose
        /// compilation assembly Unity lists (none is listed while the Editor compiles).
        /// </summary>
        internal async Task<IReadOnlyList<string>> CollectAsync(CancellationToken ct)
        {
            // Why the main thread: the compilation pipeline answers only there.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            IReadOnlyList<string> names = _readLedger(_projectRoot);
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(_projectRoot);
            int count = Math.Min(names.Count, HotReloadWarmUpContextSource.MaxWarmedTargets);
            for (int index = 0; index < count; index++)
            {
                string name = names[index];
                string fullTargetDllPath = Path.GetFullPath(layout.DllPath(name));
                // Renamed or removed since a run recorded it.
                if (!File.Exists(fullTargetDllPath))
                {
                    continue;
                }

                UnityCompilationAssembly assembly = _findCompilationAssembly(name);
                if (assembly == null || assembly.allReferences == null)
                {
                    continue;
                }

                // Read here: the pool thread must not touch the Unity object.
                string[] allReferences = assembly.allReferences;
                string scriptAssembliesDirectory = layout.CompiledAssembliesDirectory;
                // Why only the first usable name: the references bound as they are hardly differ
                // between assemblies, and the worker keeps every one it loads for its lifetime.
                // Why off the main thread: one File.Exists per reference, several hundred on a
                // large project.
                return await Task.Run(
                        () => ListReferencesBoundAsTheyAre(allReferences, fullTargetDllPath, scriptAssembliesDirectory),
                        ct)
                    .ConfigureAwait(false);
            }

            return Array.Empty<string>();
        }

        private IReadOnlyList<string> ListReferencesBoundAsTheyAre(
            string[] allReferences,
            string fullTargetDllPath,
            string scriptAssembliesDirectory)
        {
            List<string> paths = new List<string>();
            foreach (ShimCompileReference entry in HotReloadShimReferenceBuilder.ClassifyCompileReferences(
                         allReferences,
                         _projectRoot,
                         fullTargetDllPath,
                         scriptAssembliesDirectory))
            {
                if (!entry.UsesRewrittenCopy)
                {
                    paths.Add(entry.FullPath);
                }
            }

            return paths;
        }
    }
}
