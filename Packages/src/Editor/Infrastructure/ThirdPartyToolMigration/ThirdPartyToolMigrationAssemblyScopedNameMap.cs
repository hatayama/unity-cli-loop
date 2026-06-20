using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds assembly-scoped alias and declared-type lookup tables for migration planning.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyScopedNameMap
    {
        internal static MigrationAssemblyUsage CreateMigrationAssemblyUsage(
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedLegacyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentApplicationDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentApplicationAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
        {
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyDirectories != null,
                "assemblyScopedLegacyDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationDirectories != null,
                "assemblyScopedCurrentApplicationDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsDirectories != null,
                "assemblyScopedCurrentFirstPartyToolsDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyAliasesByDirectory != null,
                "assemblyScopedLegacyAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedLegacyToolInfoAliasesByDirectory != null,
                "assemblyScopedLegacyToolInfoAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentApplicationAliasesByDirectory != null,
                "assemblyScopedCurrentApplicationAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory != null,
                "assemblyScopedCurrentFirstPartyToolsAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                domainReferenceAssemblyDirectories != null,
                "domainReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            return new MigrationAssemblyUsage(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentApplicationDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyAliasesByDirectory),
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyToolInfoAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyScopedCurrentApplicationAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyScopedCurrentFirstPartyToolsAliasesByDirectory),
                CreateAssemblyScopedNamesByDirectory(assemblyDeclaredTypeNamesByDirectory),
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);
        }

        internal static void AddAssemblyScopedLegacyAliases(
            Dictionary<string, HashSet<string>> aliasesByDirectory,
            string assemblyDirectory,
            string[] aliases)
        {
            AddAssemblyScopedNames(aliasesByDirectory, assemblyDirectory, aliases);
        }

        internal static void AddAssemblyScopedNames(
            Dictionary<string, HashSet<string>> namesByDirectory,
            string assemblyDirectory,
            string[] names)
        {
            Debug.Assert(namesByDirectory != null, "namesByDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(names != null, "names must not be null");

            if (names.Length == 0)
            {
                return;
            }

            if (!namesByDirectory.TryGetValue(assemblyDirectory, out HashSet<string> nameSet))
            {
                nameSet = new HashSet<string>(StringComparer.Ordinal);
                namesByDirectory.Add(assemblyDirectory, nameSet);
            }

            foreach (string name in names)
            {
                nameSet.Add(name);
            }
        }

        internal static Dictionary<string, string[]> CreateAssemblyScopedLegacyAliasesByDirectory(
            Dictionary<string, HashSet<string>> aliasesByDirectory)
        {
            return CreateAssemblyScopedNamesByDirectory(aliasesByDirectory);
        }

        internal static Dictionary<string, string[]> CreateAssemblyScopedNamesByDirectory(
            Dictionary<string, HashSet<string>> namesByDirectory)
        {
            Debug.Assert(namesByDirectory != null, "namesByDirectory must not be null");

            Dictionary<string, string[]> result = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, HashSet<string>> namesForDirectory in namesByDirectory)
            {
                result.Add(
                    namesForDirectory.Key,
                    namesForDirectory.Value.OrderBy(name => name, StringComparer.Ordinal).ToArray());
            }

            return result;
        }

        internal static string[] GetAssemblyScopedNames(
            Dictionary<string, HashSet<string>> namesByDirectory,
            string assemblyDirectory)
        {
            Debug.Assert(namesByDirectory != null, "namesByDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");

            if (!namesByDirectory.TryGetValue(assemblyDirectory, out HashSet<string> names))
            {
                return Array.Empty<string>();
            }

            return names
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetAllAssemblyScopedLegacyToolInfoAliases(MigrationAssemblyUsage assemblyUsage)
        {
            return assemblyUsage.AssemblyScopedLegacyToolInfoAliasesByDirectory.Values
                .SelectMany(aliases => aliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
