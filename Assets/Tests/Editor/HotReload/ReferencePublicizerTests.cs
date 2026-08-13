using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mono.Cecil;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="ReferencePublicizer"/> cache path and visibility rewrite.
    /// </summary>
    public class ReferencePublicizerTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike.SpikePrivateAccessFixture";

        /// <summary>
        /// What: publicizing the test assembly exposes private fields/methods and reuses the
        /// cached copy on a second call (same path; last-write time stays at a planted marker).
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_RewritesPrivateMembersAndCachesByMvid()
        {
            string sourceDllPath = ResolveTestAssemblyDllPath();
            IReadOnlyCollection<string> searchDirectories = PublicizerTestSearchDirectories.ForHotReloadTestAssembly();
            string firstPath = ReferencePublicizer.GetOrCreatePublicizedCopy(sourceDllPath, searchDirectories);

            // Plant a distinctive mtime: a cache hit must leave it alone, while a regenerate
            // would rewrite the file and replace this marker with "now". Avoids Thread.Sleep
            // (banned on the EditMode main thread) while still proving the early-return path.
            DateTime markerWriteTimeUtc = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(firstPath, markerWriteTimeUtc);

            string secondPath = ReferencePublicizer.GetOrCreatePublicizedCopy(sourceDllPath, searchDirectories);

            Assert.That(firstPath, Is.EqualTo(secondPath), "Second call must reuse the cached publicized copy.");
            Assert.That(
                File.GetLastWriteTimeUtc(secondPath),
                Is.EqualTo(markerWriteTimeUtc),
                "Cache hit must not rewrite the file (LastWriteTimeUtc would leave the planted marker).");
            Assert.That(File.Exists(firstPath), Is.True, "Cached publicized DLL must exist on disk.");
            Assert.That(
                firstPath.Replace('\\', '/'),
                Does.Contain("/Library/UloopHotReload/PublicizedRefs/"),
                "Cache must live under Library/UloopHotReload/PublicizedRefs/.");
            Assert.That(
                Path.GetFileName(firstPath),
                Does.StartWith(TestAssemblyName + "-"),
                "Cache file name must start with the assembly name and Mvid.");

            using AssemblyDefinition publicizedAssembly = AssemblyDefinition.ReadAssembly(firstPath);
            Assert.That(publicizedAssembly.Name.Name, Is.EqualTo(TestAssemblyName));

            TypeDefinition fixtureType = publicizedAssembly.MainModule.GetType(FixtureTypeFullName);
            Assert.That(fixtureType, Is.Not.Null, $"Type not found: {FixtureTypeFullName}");
            Assert.That(fixtureType.IsPublic || fixtureType.IsNestedPublic, Is.True);

            FieldDefinition counterField = fixtureType.Fields.First(field => field.Name == "_counter");
            Assert.That(
                (counterField.Attributes & CecilFieldAttributes.FieldAccessMask),
                Is.EqualTo(CecilFieldAttributes.Public),
                "_counter must be public after publicize.");

            MethodDefinition bumpMethod = fixtureType.Methods.First(method => method.Name == "BumpByOne");
            Assert.That(
                (bumpMethod.Attributes & CecilMethodAttributes.MemberAccessMask),
                Is.EqualTo(CecilMethodAttributes.Public),
                "BumpByOne must be public after publicize.");

            AssertNoNonPublicTypesOrMembersRemain(publicizedAssembly);
        }

        /// <summary>
        /// What: pruning stale publicized copies does not delete hyphenated sibling assembly
        /// caches that share a prefix (e.g. Assembly-CSharp vs Assembly-CSharp-Editor).
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_DoesNotDeleteHyphenatedSiblingAssemblyCaches()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputDirectory = Path.Combine(projectRootPath, HotReloadConstants.PublicizedRefsRelativeDirectory);
            Directory.CreateDirectory(outputDirectory);

            // Force the write+prune path: remove existing exact-mvid caches for this assembly.
            DeleteExactMvidCachesForAssembly(outputDirectory, TestAssemblyName);

            string siblingCachePath = Path.Combine(
                outputDirectory,
                TestAssemblyName + "-Sibling-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(siblingCachePath, new byte[] { 0x4D, 0x5A });

            string staleSameAssemblyPath = Path.Combine(
                outputDirectory,
                TestAssemblyName + "-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(staleSameAssemblyPath, new byte[] { 0x4D, 0x5A });

            string publicizedPath = ReferencePublicizer.GetOrCreatePublicizedCopy(
                ResolveTestAssemblyDllPath(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());

            Assert.That(File.Exists(publicizedPath), Is.True, "Current-mvid publicized copy must be written.");
            Assert.That(
                File.Exists(siblingCachePath),
                Is.True,
                "Hyphenated sibling assembly caches must survive prune (prefix glob alone is insufficient).");
            Assert.That(
                File.Exists(staleSameAssemblyPath),
                Is.False,
                "A true stale same-assembly cache (name-<mvid>.dll) must still be pruned.");
        }

        private static void DeleteExactMvidCachesForAssembly(string outputDirectory, string assemblyName)
        {
            foreach (string candidatePath in Directory.GetFiles(outputDirectory, assemblyName + "-*.dll"))
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidatePath);
                if (fileNameWithoutExtension.Length <= assemblyName.Length + 1)
                {
                    continue;
                }

                string mvidCandidate = fileNameWithoutExtension.Substring(assemblyName.Length + 1);
                if (Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    File.Delete(candidatePath);
                }
            }
        }

        private static void AssertNoNonPublicTypesOrMembersRemain(AssemblyDefinition assemblyDefinition)
        {
            foreach (TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
            {
                if (type.Name == "<Module>")
                {
                    continue;
                }

                bool typeIsPublic = type.IsNested ? type.IsNestedPublic : type.IsPublic;
                Assert.That(typeIsPublic, Is.True, $"Type still non-public: {type.FullName}");

                foreach (FieldDefinition field in type.Fields)
                {
                    bool isEventBackingField = false;
                    foreach (EventDefinition eventDefinition in type.Events)
                    {
                        if (eventDefinition.Name == field.Name)
                        {
                            isEventBackingField = true;
                        }
                    }

                    if (isEventBackingField)
                    {
                        Assert.That(
                            (field.Attributes & CecilFieldAttributes.FieldAccessMask),
                            Is.Not.EqualTo(CecilFieldAttributes.Public),
                            $"Event backing field must stay non-public: {type.FullName}.{field.Name}");
                        continue;
                    }

                    Assert.That(
                        (field.Attributes & CecilFieldAttributes.FieldAccessMask),
                        Is.EqualTo(CecilFieldAttributes.Public),
                        $"Field still non-public: {type.FullName}.{field.Name}");
                }

                foreach (MethodDefinition method in type.Methods)
                {
                    Assert.That(
                        (method.Attributes & CecilMethodAttributes.MemberAccessMask),
                        Is.EqualTo(CecilMethodAttributes.Public),
                        $"Method still non-public: {type.FullName}.{method.Name}");
                }
            }
        }

        /// <summary>
        /// What: publicizing keeps a field-like event's backing field non-public while its
        /// add/remove accessors become public, so shims can subscribe without CS0229.
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_KeepsEventBackingFieldNonPublic()
        {
            string publicizedPath = ReferencePublicizer.GetOrCreatePublicizedCopy(
                ResolveTestAssemblyDllPath(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());
            using AssemblyDefinition publicizedAssembly = AssemblyDefinition.ReadAssembly(publicizedPath);

            const string eventFixtureTypeFullName =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadEventFixture";
            TypeDefinition fixtureType = publicizedAssembly.MainModule.GetType(eventFixtureTypeFullName);
            Assert.That(fixtureType, Is.Not.Null, $"Type not found: {eventFixtureTypeFullName}");

            FieldDefinition scoreChangedField = fixtureType.Fields.First(field => field.Name == "ScoreChanged");
            Assert.That(
                (scoreChangedField.Attributes & CecilFieldAttributes.FieldAccessMask),
                Is.Not.EqualTo(CecilFieldAttributes.Public),
                "Event backing field ScoreChanged must stay non-public.");

            MethodDefinition addAccessor = fixtureType.Methods.First(method => method.Name == "add_ScoreChanged");
            MethodDefinition removeAccessor = fixtureType.Methods.First(method => method.Name == "remove_ScoreChanged");
            Assert.That(
                (addAccessor.Attributes & CecilMethodAttributes.MemberAccessMask),
                Is.EqualTo(CecilMethodAttributes.Public),
                "add_ScoreChanged must be public after publicize.");
            Assert.That(
                (removeAccessor.Attributes & CecilMethodAttributes.MemberAccessMask),
                Is.EqualTo(CecilMethodAttributes.Public),
                "remove_ScoreChanged must be public after publicize.");
        }

        private static string ResolveTestAssemblyDllPath()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(projectRootPath, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
            Assert.That(File.Exists(dllPath), Is.True, $"Test assembly dll not found: {dllPath}");
            return dllPath;
        }
    }
}
