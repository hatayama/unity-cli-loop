using System;
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
            string firstPath = ReferencePublicizer.GetOrCreatePublicizedCopy(sourceDllPath);

            // Plant a distinctive mtime: a cache hit must leave it alone, while a regenerate
            // would rewrite the file and replace this marker with "now". Avoids Thread.Sleep
            // (banned on the EditMode main thread) while still proving the early-return path.
            DateTime markerWriteTimeUtc = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(firstPath, markerWriteTimeUtc);

            string secondPath = ReferencePublicizer.GetOrCreatePublicizedCopy(sourceDllPath);

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

        private static string ResolveTestAssemblyDllPath()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(projectRootPath, "Library", "ScriptAssemblies", TestAssemblyName + ".dll");
            Assert.That(File.Exists(dllPath), Is.True, $"Test assembly dll not found: {dllPath}");
            return dllPath;
        }
    }
}
