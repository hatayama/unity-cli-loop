using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

using UnityEngine;

using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Produces a Cecil visibility rewrite of a project script assembly so external csc can
    /// compile shim sources that reference private/internal members. Publicize is a compile-time
    /// aid only — Editor Mono still enforces accessibility at JIT time.
    /// </summary>
    internal static class ReferencePublicizer
    {
        /// <summary>
        /// Collects distinct directory paths of existing DLL references for Cecil
        /// <see cref="DefaultAssemblyResolver"/> search. Null <paramref name="referencePaths"/>
        /// yields an empty set so callers need not special-case Unity's null allReferences.
        /// </summary>
        internal static IReadOnlyCollection<string> CollectResolverSearchDirectories(
            IReadOnlyCollection<string> referencePaths)
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.Ordinal);
            if (referencePaths == null)
            {
                return directories;
            }

            foreach (string reference in referencePaths)
            {
                if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                {
                    continue;
                }

                string directory = Path.GetDirectoryName(Path.GetFullPath(reference));
                if (!string.IsNullOrEmpty(directory))
                {
                    directories.Add(directory);
                }
            }

            return directories;
        }

        /// <summary>
        /// Returns the path of a cached publicized copy for <paramref name="sourceDllPath"/>,
        /// writing it on first use. Only <c>Library/ScriptAssemblies/</c> DLLs are accepted —
        /// engine and system assemblies must not be rewritten.
        /// <paramref name="resolverSearchDirectories"/> are extra Cecil search dirs derived by
        /// the caller from compilation references (Unity Editor layout must not be hardcoded).
        /// </summary>
        public static string GetOrCreatePublicizedCopy(
            string sourceDllPath,
            IReadOnlyCollection<string> resolverSearchDirectories)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceDllPath), "sourceDllPath must not be null or empty.");
            Debug.Assert(resolverSearchDirectories != null, "resolverSearchDirectories must not be null.");

            string fullSourceDllPath = Path.GetFullPath(sourceDllPath);
            Debug.Assert(File.Exists(fullSourceDllPath), "sourceDllPath must point to an existing DLL.");
            AssertIsScriptAssemblyPath(fullSourceDllPath);

            // InMemory: the source DLL is the currently loaded script assembly; keep no file handle.
            // A search-path resolver is required so Cecil can satisfy assembly refs while rewriting
            // (missing mscorlib/netstandard otherwise throws AssemblyResolutionException on Write).
            using DefaultAssemblyResolver assemblyResolver = CreateAssemblyResolver(
                fullSourceDllPath,
                resolverSearchDirectories);
            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                AssemblyResolver = assemblyResolver
            };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(fullSourceDllPath, readerParameters);

            string assemblyName = assemblyDefinition.Name.Name;
            string mvid = assemblyDefinition.MainModule.Mvid.ToString("N");
            string outputDirectory = ResolvePublicizedRefsDirectory();
            Directory.CreateDirectory(outputDirectory);

            string outputDllPath = Path.Combine(
                outputDirectory,
                assemblyName + "-" + mvid + HotReloadConstants.CompiledAssemblyExtension);
            if (File.Exists(outputDllPath))
            {
                return outputDllPath;
            }

            // An Mvid change means the assembly already reloaded; no in-flight compile can still
            // need the previous publicized copy, so drop stale siblings before writing the new one.
            DeleteStalePublicizedCopies(outputDirectory, assemblyName, outputDllPath);

            foreach (ModuleDefinition module in assemblyDefinition.Modules)
            {
                foreach (TypeDefinition type in module.GetTypes())
                {
                    // <Module> is a metadata artifact; rewriting its visibility breaks the module.
                    if (type.Name == "<Module>")
                    {
                        continue;
                    }

                    PublicizeType(type);
                }
            }

            assemblyDefinition.Write(outputDllPath);
            return outputDllPath;
        }

        private static void DeleteStalePublicizedCopies(
            string outputDirectory,
            string assemblyName,
            string currentOutputDllPath)
        {
            string searchPattern = assemblyName + "-*" + HotReloadConstants.CompiledAssemblyExtension;
            string currentOutputFullPath = Path.GetFullPath(currentOutputDllPath);
            foreach (string candidatePath in Directory.GetFiles(outputDirectory, searchPattern))
            {
                if (string.Equals(
                        Path.GetFullPath(candidatePath),
                        currentOutputFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The glob is a prefix match, so hyphenated siblings such as
                // Assembly-CSharp-Editor-<mvid>.dll also match Assembly-CSharp-*.dll.
                // Only delete when the suffix after "<assemblyName>-" is exactly an Mvid in "N"
                // format — never a longer sibling assembly name.
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidatePath);
                if (fileNameWithoutExtension.Length <= assemblyName.Length + 1)
                {
                    continue;
                }

                string mvidCandidate = fileNameWithoutExtension.Substring(assemblyName.Length + 1);
                if (!Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    continue;
                }

                File.Delete(candidatePath);
            }
        }

        private static void AssertIsScriptAssemblyPath(string fullSourceDllPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAssembliesDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            string normalizedSource = NormalizePathForComparison(fullSourceDllPath);
            string normalizedDirectory = NormalizePathForComparison(scriptAssembliesDirectory);
            // Windows paths are case-insensitive; separators are normalized to '/' above.
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            bool underScriptAssemblies = normalizedSource.StartsWith(normalizedDirectory + "/", comparison);

            Debug.Assert(
                underScriptAssemblies,
                "ReferencePublicizer only accepts DLLs under Library/ScriptAssemblies/.");
        }

        private static string ResolvePublicizedRefsDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, HotReloadConstants.PublicizedRefsRelativeDirectory);
        }

        private static DefaultAssemblyResolver CreateAssemblyResolver(
            string sourceDllPath,
            IReadOnlyCollection<string> resolverSearchDirectories)
        {
            Debug.Assert(resolverSearchDirectories != null, "resolverSearchDirectories must not be null.");

            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(sourceDllPath));

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            resolver.AddSearchDirectory(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            foreach (string searchDirectory in resolverSearchDirectories)
            {
                if (string.IsNullOrEmpty(searchDirectory) || !Directory.Exists(searchDirectory))
                {
                    continue;
                }

                resolver.AddSearchDirectory(searchDirectory);
            }

            // Why not: walk AppDomain assemblies for extra search dirs — Assembly.Load(byte[])
            // shims throw NotSupportedException on .Location, and hot reload loads those shims into
            // the same domain. Search directories come from the caller's compilation references
            // instead; hardcoding Editor Contents Managed paths fails on Unity 6 layouts.

            return resolver;
        }

        private static void PublicizeType(TypeDefinition type)
        {
            // Preserve non-visibility flags (abstract, sealed, interface, …); only swap the
            // visibility bits so the rewrite stays a pure accessibility change.
            if (type.IsNested)
            {
                type.Attributes = (type.Attributes & ~CecilTypeAttributes.VisibilityMask) | CecilTypeAttributes.NestedPublic;
            }
            else
            {
                type.Attributes = (type.Attributes & ~CecilTypeAttributes.VisibilityMask) | CecilTypeAttributes.Public;
            }

            foreach (FieldDefinition field in type.Fields)
            {
                // A field-like event's compiler-generated backing field shares the event's name.
                // Publicizing it makes both the event (via its publicized accessors) and the field
                // visible, so every shim touching the event fails with CS0229 (ambiguous reference).
                // The backing field keeps its original accessibility; shims subscribe through the
                // public add/remove accessors instead.
                if (HasEventNamed(type, field.Name))
                {
                    continue;
                }

                field.Attributes = (field.Attributes & ~CecilFieldAttributes.FieldAccessMask) | CecilFieldAttributes.Public;
            }

            // Property/event accessors are MethodDefinitions on the type, so this loop covers them.
            foreach (MethodDefinition method in type.Methods)
            {
                method.Attributes = (method.Attributes & ~CecilMethodAttributes.MemberAccessMask) | CecilMethodAttributes.Public;
            }
        }

        private static bool HasEventNamed(TypeDefinition type, string fieldName)
        {
            foreach (EventDefinition eventDefinition in type.Events)
            {
                if (eventDefinition.Name == fieldName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePathForComparison(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
