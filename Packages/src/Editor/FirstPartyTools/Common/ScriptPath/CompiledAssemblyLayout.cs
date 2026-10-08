using System;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides where the compiled assemblies of a project root live. An ordinary project reads its
    /// own Library/ScriptAssemblies; a Multiplayer Play Mode Virtual Player, whose root is
    /// &lt;main project&gt;/Library/VP/&lt;player&gt;, reads the main project's, because it has none
    /// of its own.
    /// </summary>
    internal sealed class CompiledAssemblyLayout
    {
        // Why these are spelled here: this assembly cannot reference hot reload's constants, since
        // the dependency runs from hot reload to this assembly only.
        private const string LibraryDirectoryName = "Library";
        private const string VirtualPlayersDirectoryName = "VP";
        private const string ScriptAssembliesDirectoryName = "ScriptAssemblies";

        private CompiledAssemblyLayout(
            string projectRoot,
            bool isVirtualPlayer,
            string mainProjectRoot,
            string compiledAssembliesDirectory)
        {
            ProjectRoot = projectRoot;
            IsVirtualPlayer = isVirtualPlayer;
            MainProjectRoot = mainProjectRoot;
            CompiledAssembliesDirectory = compiledAssembliesDirectory;
        }

        /// <summary>The project root made absolute, without a trailing separator.</summary>
        internal string ProjectRoot { get; }

        /// <summary>True when ProjectRoot is a Multiplayer Play Mode Virtual Player's root.</summary>
        internal bool IsVirtualPlayer { get; }

        /// <summary>The main project's root for a Virtual Player; ProjectRoot otherwise.</summary>
        internal string MainProjectRoot { get; }

        /// <summary>The absolute directory the project's compiled assemblies are read from.</summary>
        internal string CompiledAssembliesDirectory { get; }

        internal static CompiledAssemblyLayout Resolve(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");

            string fullRoot = Path.GetFullPath(projectRoot);
            // Why trim first: with a trailing separator, Path.GetDirectoryName returns the same
            // directory, so every parent lookup below would land one level too low. A path root ("/"
            // or "C:\") stays as it is, because trimming it would leave "" or the drive-relative "C:".
            bool isPathRoot = string.Equals(Path.GetPathRoot(fullRoot), fullRoot, StringComparison.Ordinal);
            string root = isPathRoot
                ? fullRoot
                : fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string virtualPlayersDirectory = Path.GetDirectoryName(root);
            string libraryDirectory = string.IsNullOrEmpty(virtualPlayersDirectory)
                ? null
                : Path.GetDirectoryName(virtualPlayersDirectory);
            string mainRoot = string.IsNullOrEmpty(libraryDirectory) ? null : Path.GetDirectoryName(libraryDirectory);
            // Why a main root is required: /Library/VP/x has no project above Library to read from.
            bool isVirtualPlayer = !string.IsNullOrEmpty(mainRoot)
                && string.Equals(Path.GetFileName(virtualPlayersDirectory), VirtualPlayersDirectoryName, StringComparison.Ordinal)
                && string.Equals(Path.GetFileName(libraryDirectory), LibraryDirectoryName, StringComparison.Ordinal);
            string compiledAssembliesOwner = isVirtualPlayer ? mainRoot : root;
            return new CompiledAssemblyLayout(
                root,
                isVirtualPlayer,
                compiledAssembliesOwner,
                Path.Combine(compiledAssembliesOwner, LibraryDirectoryName, ScriptAssembliesDirectoryName));
        }

        internal string DllPath(string assemblyName)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            return Path.Combine(CompiledAssembliesDirectory, assemblyName + ".dll");
        }

        internal string PdbPath(string assemblyName)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            return Path.Combine(CompiledAssembliesDirectory, assemblyName + ".pdb");
        }
    }
}
