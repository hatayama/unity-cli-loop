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
        private const string InternalsVisibleToAttributeFullName =
            "System.Runtime.CompilerServices.InternalsVisibleToAttribute";

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
        /// Returns the path of a cached publicized copy of <paramref name="home"/>'s image,
        /// writing it on first use. Only a publicizable home is accepted, and its image must sit
        /// under <c>Library/ScriptAssemblies/</c> or <c>Library/UloopHotReload/IntroducedTypes/</c>
        /// — engine and system assemblies must not be rewritten.
        /// <paramref name="resolverSearchDirectories"/> are extra Cecil search dirs derived by the
        /// caller from compilation references (Unity Editor layout must not be hardcoded).
        /// </summary>
        public static string GetOrCreatePublicizedCopy(
            HotReloadTypeHome home,
            IReadOnlyCollection<string> resolverSearchDirectories)
        {
            return GetOrCreateRewrittenCopy(
                home,
                resolverSearchDirectories,
                assemblyDefinition => WriteOrReuseRewrittenCopy(
                    assemblyDefinition,
                    HotReloadConstants.PublicizedRefsRelativeDirectory,
                    PublicizeType));
        }

        /// <summary>
        /// Returns the path of a cached copy of <paramref name="home"/>'s image for a shim compile
        /// of an edit in the assembly named <paramref name="shimTargetAssemblyName"/>, writing it
        /// on first use. When the image grants that assembly its internals, this is the publicized
        /// copy of <see cref="GetOrCreatePublicizedCopy"/>. Otherwise the image's internal top-level
        /// types and its private, internal and private protected members stay as they are, because
        /// the edited assembly's own compile never saw them. The same preconditions on
        /// <paramref name="home"/> apply.
        /// </summary>
        public static string GetOrCreateShimReferenceCopy(
            HotReloadTypeHome home,
            IReadOnlyCollection<string> resolverSearchDirectories,
            string shimTargetAssemblyName)
        {
            Debug.Assert(
                !string.IsNullOrEmpty(shimTargetAssemblyName),
                "shimTargetAssemblyName must not be null or empty.");

            return GetOrCreateRewrittenCopy(
                home,
                resolverSearchDirectories,
                assemblyDefinition => GrantsInternalsTo(assemblyDefinition, shimTargetAssemblyName)
                    ? WriteOrReuseRewrittenCopy(
                        assemblyDefinition,
                        HotReloadConstants.PublicizedRefsRelativeDirectory,
                        PublicizeType)
                    : WriteOrReuseRewrittenCopy(
                        assemblyDefinition,
                        HotReloadConstants.PublicizedExternalRefsRelativeDirectory,
                        PublicizeTypeKeepingHiddenMembers));
        }

        /// <summary>
        /// Returns the assembly simple name an InternalsVisibleTo argument grants internals to:
        /// the text before the first comma (which starts an optional public key), trimmed.
        /// </summary>
        internal static string ParseFriendAssemblyName(string friendAssemblyName)
        {
            Debug.Assert(friendAssemblyName != null, "friendAssemblyName must not be null.");

            int commaIndex = friendAssemblyName.IndexOf(',');
            string simpleName = commaIndex < 0
                ? friendAssemblyName
                : friendAssemblyName.Substring(0, commaIndex);
            return simpleName.Trim();
        }

        internal static string GetOrCreateInternalsExposedCopy(
            HotReloadTypeHome home,
            IReadOnlyCollection<string> resolverSearchDirectories)
        {
            return GetOrCreateRewrittenCopy(
                home,
                resolverSearchDirectories,
                assemblyDefinition => WriteOrReuseRewrittenCopy(
                    assemblyDefinition,
                    HotReloadConstants.InternalsExposedRefsRelativeDirectory,
                    ExposeInternalsOfType));
        }

        // Reads home's image and lets writeOrReuseCopy pick and return the copy. Why the variant is
        // picked after reading: the cache path needs the image's Mvid, so the image is read before
        // any cache lookup anyway, and its own attributes decide the variant at no extra read.
        private static string GetOrCreateRewrittenCopy(
            HotReloadTypeHome home,
            IReadOnlyCollection<string> resolverSearchDirectories,
            Func<AssemblyDefinition, string> writeOrReuseCopy)
        {
            Debug.Assert(home != null, "home must not be null.");
            Debug.Assert(home.IsPublicizable, "home must be publicizable.");
            Debug.Assert(resolverSearchDirectories != null, "resolverSearchDirectories must not be null.");

            string fullSourceDllPath = Path.GetFullPath(home.DllPath);
            Debug.Assert(File.Exists(fullSourceDllPath), "home.DllPath must point to an existing DLL.");
            AssertIsPublicizableSourcePath(fullSourceDllPath);

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
            return writeOrReuseCopy(assemblyDefinition);
        }

        private static string WriteOrReuseRewrittenCopy(
            AssemblyDefinition assemblyDefinition,
            string outputRelativeDirectory,
            Action<TypeDefinition> rewriteType)
        {
            string assemblyName = assemblyDefinition.Name.Name;
            string mvid = assemblyDefinition.MainModule.Mvid.ToString("N");
            string outputDirectory = ResolveOutputDirectory(outputRelativeDirectory);
            Directory.CreateDirectory(outputDirectory);

            string outputDllPath = Path.Combine(
                outputDirectory,
                assemblyName + "-" + mvid + HotReloadConstants.CompiledAssemblyExtension);
            if (File.Exists(outputDllPath) && new FileInfo(outputDllPath).Length > 0)
            {
                return outputDllPath;
            }

            // Why: older versions wrote the publicized DLL in place; a failed Write could leave
            // a 0-byte file that File.Exists alone treated as a valid cache hit. Delete it so
            // this call regenerates instead of poisoning later shim compiles.
            if (File.Exists(outputDllPath))
            {
                File.Delete(outputDllPath);
            }

            // An Mvid change means the assembly already reloaded; no in-flight compile can still
            // need the previous publicized copy, so drop stale siblings before writing the new one.
            DeleteStaleCopies(outputDirectory, assemblyName, outputDllPath);

            foreach (ModuleDefinition module in assemblyDefinition.Modules)
            {
                foreach (TypeDefinition type in module.GetTypes())
                {
                    // <Module> is a metadata artifact; rewriting its visibility breaks the module.
                    if (type.Name == "<Module>")
                    {
                        continue;
                    }

                    rewriteType(type);
                }
            }

            // Write to a temp path then Move so a thrown Write cannot leave a 0-byte final cache.
            string tempDllPath = outputDllPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                assemblyDefinition.Write(tempDllPath);
                File.Move(tempDllPath, outputDllPath);
            }
            finally
            {
                if (File.Exists(tempDllPath))
                {
                    File.Delete(tempDllPath);
                }
            }

            return outputDllPath;
        }

        private static void DeleteStaleCopies(
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

        // Why two directories and not one: a shim compiled for an edited body of a type an
        // earlier reload introduced has to read that type's private members, and the image that
        // holds it is the retained artifact rather than a compiled script assembly.
        private static void AssertIsPublicizableSourcePath(string fullSourceDllPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Windows paths are case-insensitive; separators are normalized to '/' below.
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string normalizedSource = NormalizePathForComparison(fullSourceDllPath);
            bool underAcceptedDirectory = IsUnderProjectDirectory(
                    normalizedSource,
                    projectRoot,
                    HotReloadConstants.ScriptAssembliesRelativeDirectory,
                    comparison)
                || IsUnderProjectDirectory(
                    normalizedSource,
                    projectRoot,
                    HotReloadConstants.IntroducedTypeArtifactsRelativeDirectory,
                    comparison);

            Debug.Assert(
                underAcceptedDirectory,
                "ReferencePublicizer only accepts DLLs under Library/ScriptAssemblies/ or "
                + "Library/UloopHotReload/IntroducedTypes/.");
        }

        private static bool IsUnderProjectDirectory(
            string normalizedSourcePath,
            string projectRoot,
            string relativeDirectory,
            StringComparison comparison)
        {
            string normalizedDirectory = NormalizePathForComparison(
                Path.GetFullPath(Path.Combine(projectRoot, relativeDirectory)));
            return normalizedSourcePath.StartsWith(normalizedDirectory + "/", comparison);
        }

        private static string ResolveOutputDirectory(string relativeDirectory)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, relativeDirectory);
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

        private static void ExposeInternalsOfType(TypeDefinition type)
        {
            CecilTypeAttributes visibility = type.Attributes & CecilTypeAttributes.VisibilityMask;
            CecilTypeAttributes exposedVisibility = visibility switch
            {
                CecilTypeAttributes.NotPublic => CecilTypeAttributes.Public,
                CecilTypeAttributes.NestedAssembly => CecilTypeAttributes.NestedPublic,
                CecilTypeAttributes.NestedFamORAssem => CecilTypeAttributes.NestedPublic,
                CecilTypeAttributes.NestedFamANDAssem => CecilTypeAttributes.NestedFamily,
                _ => visibility
            };
            type.Attributes = (type.Attributes & ~CecilTypeAttributes.VisibilityMask) | exposedVisibility;

            foreach (FieldDefinition field in type.Fields)
            {
                CecilFieldAttributes access = field.Attributes & CecilFieldAttributes.FieldAccessMask;
                CecilFieldAttributes exposedAccess = access switch
                {
                    CecilFieldAttributes.Assembly => CecilFieldAttributes.Public,
                    CecilFieldAttributes.FamORAssem => CecilFieldAttributes.Public,
                    CecilFieldAttributes.FamANDAssem => CecilFieldAttributes.Family,
                    _ => access
                };
                field.Attributes = (field.Attributes & ~CecilFieldAttributes.FieldAccessMask) | exposedAccess;
            }

            // Accessors are ordinary metadata methods. Private event backing fields stay private
            // through their own flags; only the full publicizer needs an event-name exception.
            foreach (MethodDefinition method in type.Methods)
            {
                CecilMethodAttributes access = method.Attributes & CecilMethodAttributes.MemberAccessMask;
                CecilMethodAttributes exposedAccess = access switch
                {
                    CecilMethodAttributes.Assembly => CecilMethodAttributes.Public,
                    CecilMethodAttributes.FamORAssem => CecilMethodAttributes.Public,
                    CecilMethodAttributes.FamANDAssem => CecilMethodAttributes.Family,
                    _ => access
                };
                method.Attributes = (method.Attributes & ~CecilMethodAttributes.MemberAccessMask) | exposedAccess;
            }
        }

        private static void PublicizeType(TypeDefinition type)
        {
            PublicizeTypeVisibility(type);
            PublicizeMembers(type, keepHiddenMembers: false);
        }

        // Why top-level internal types and hidden members stay as they are: the edited assembly's own
        // compile never saw them, and a shim compile that does can find a call ambiguous, as when an
        // internal type declares an extension method with the signature of a public one elsewhere,
        // or bind it to a more specific private overload the compiled method never called. Nested
        // types are still publicized: they are reached only through their enclosing type and cannot
        // declare extension methods.
        private static void PublicizeTypeKeepingHiddenMembers(TypeDefinition type)
        {
            if (!type.IsNested
                && (type.Attributes & CecilTypeAttributes.VisibilityMask) == CecilTypeAttributes.NotPublic)
            {
                return;
            }

            PublicizeTypeVisibility(type);
            PublicizeMembers(type, keepHiddenMembers: true);
        }

        private static void PublicizeTypeVisibility(TypeDefinition type)
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
        }

        // keepHiddenMembers leaves private, internal and private protected members as they are.
        // Protected members are publicized either way: a shim calls a base type's protected members
        // from outside the type hierarchy.
        private static void PublicizeMembers(TypeDefinition type, bool keepHiddenMembers)
        {
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

                CecilFieldAttributes access = field.Attributes & CecilFieldAttributes.FieldAccessMask;
                if (keepHiddenMembers && FieldAccessIsHiddenFromOtherAssemblies(access))
                {
                    continue;
                }

                field.Attributes = (field.Attributes & ~CecilFieldAttributes.FieldAccessMask) | CecilFieldAttributes.Public;
            }

            // Property/event accessors are MethodDefinitions on the type, so this loop covers them.
            foreach (MethodDefinition method in type.Methods)
            {
                CecilMethodAttributes access = method.Attributes & CecilMethodAttributes.MemberAccessMask;
                if (keepHiddenMembers && MethodAccessIsHiddenFromOtherAssemblies(access))
                {
                    continue;
                }

                method.Attributes = (method.Attributes & ~CecilMethodAttributes.MemberAccessMask) | CecilMethodAttributes.Public;
            }
        }

        // Another assembly never reaches a private member, and reaches an internal or private
        // protected member only through an InternalsVisibleTo grant.
        private static bool FieldAccessIsHiddenFromOtherAssemblies(CecilFieldAttributes access)
        {
            return access == CecilFieldAttributes.Private
                || access == CecilFieldAttributes.Assembly
                || access == CecilFieldAttributes.FamANDAssem;
        }

        private static bool MethodAccessIsHiddenFromOtherAssemblies(CecilMethodAttributes access)
        {
            return access == CecilMethodAttributes.Private
                || access == CecilMethodAttributes.Assembly
                || access == CecilMethodAttributes.FamANDAssem;
        }

        // Why ignore case: the compiler matches assembly simple names without case when it honors
        // InternalsVisibleTo.
        private static bool GrantsInternalsTo(AssemblyDefinition assemblyDefinition, string targetAssemblyName)
        {
            foreach (CustomAttribute attribute in assemblyDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName != InternalsVisibleToAttributeFullName
                    || attribute.ConstructorArguments.Count == 0
                    || !(attribute.ConstructorArguments[0].Value is string friendAssemblyName))
                {
                    continue;
                }

                if (string.Equals(
                        ParseFriendAssemblyName(friendAssemblyName),
                        targetAssemblyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
