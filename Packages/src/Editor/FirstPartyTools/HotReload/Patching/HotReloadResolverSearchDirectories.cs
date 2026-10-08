using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Cecil search directories for publicizing a compilation assembly's references: the
    /// directories of the assembly's own references first, then those of every assembly it
    /// references transitively. Cecil resolves a referenced assembly while writing a publicized
    /// copy, and a precompiled DLL that only a referenced assembly lists would otherwise be
    /// unreachable (a test assembly with overrideReferences does not list the game's plugins).
    /// </summary>
    internal static class HotReloadResolverSearchDirectories
    {
        internal static IReadOnlyCollection<string> Collect(string projectRoot, UnityCompilationAssembly rootAssembly)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(rootAssembly != null, "rootAssembly must not be null.");

            List<string> orderedDirectories = new List<string>();
            HashSet<string> seenDirectories = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> seenReferences = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> visitedAssemblies = new HashSet<string>(StringComparer.Ordinal);
            Queue<UnityCompilationAssembly> pending = new Queue<UnityCompilationAssembly>();
            pending.Enqueue(rootAssembly);
            visitedAssemblies.Add(rootAssembly.name);

            // Why breadth first: the directories of the root's own references come before anything
            // a transitive reference adds, so a same-named DLL keeps resolving to the root's own copy.
            while (pending.Count > 0)
            {
                UnityCompilationAssembly current = pending.Dequeue();
                // allReferences already dereferences assemblyReferences, so a null array would have
                // thrown here; no null guard after this point.
                List<string> unseenReferences = TakeUnseenReferences(projectRoot, current.allReferences, seenReferences);
                foreach (string directory in ReferencePublicizer.CollectResolverSearchDirectories(unseenReferences))
                {
                    if (seenDirectories.Add(directory))
                    {
                        orderedDirectories.Add(directory);
                    }
                }

                foreach (UnityCompilationAssembly referenced in current.assemblyReferences)
                {
                    if (visitedAssemblies.Add(referenced.name))
                    {
                        pending.Enqueue(referenced);
                    }
                }
            }

            return orderedDirectories;
        }

        // Why skip a path seen earlier: the engine references, about 230 per assembly, repeat in
        // every assembly of the closure, and each one would cost a File.Exists. A path seen earlier
        // already added its directory at the same or a shallower level, or did not exist then either.
        // Why against the root: a Virtual Player's script assemblies are listed relative to its root
        // (../../ScriptAssemblies), and its process need not run there.
        private static List<string> TakeUnseenReferences(
            string projectRoot,
            string[] references,
            HashSet<string> seenReferences)
        {
            List<string> unseenReferences = new List<string>();
            foreach (string reference in references)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                string fullReference = Path.GetFullPath(Path.Combine(projectRoot, reference));
                if (seenReferences.Add(fullReference))
                {
                    unseenReferences.Add(fullReference);
                }
            }

            return unseenReferences;
        }
    }
}
