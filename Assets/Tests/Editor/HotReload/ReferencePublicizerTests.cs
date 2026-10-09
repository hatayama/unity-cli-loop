using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using CecilFieldAttributes = Mono.Cecil.FieldAttributes;
using CecilMethodAttributes = Mono.Cecil.MethodAttributes;
using CecilTypeAttributes = Mono.Cecil.TypeAttributes;
using ReflectionAssembly = System.Reflection.Assembly;

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

        private const string HiddenExtensionsTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceHiddenIntExtensions";

        private const string PublicHostTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferencePublicHost";

        private const string PublicBaseTypeFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferencePublicBase";

        // The test assembly grants internals to this name, and to no assembly named like the
        // stranger target.
        private const string FriendTargetAssemblyName = "UloopShimReferenceFriendTarget";

        private const string StrangerTargetAssemblyName = "UloopShimReferenceStrangerTarget";

        private const string ArtifactAssemblyName = "UloopIntroducedTypes_PublicizerFixture";

        private const string ArtifactIntroducedTypeMetadataName = "PublicizerFixture.Introduced";

        private const string ArtifactIntroducedTypeSource = @"namespace PublicizerFixture
{
    public class Introduced
    {
        private int _counter;

        public int Bump()
        {
            _counter = _counter + 1;
            return Secret();
        }

        private int Secret()
        {
            return _counter;
        }
    }
}
";

        // Why wait: GetOrCreatePublicizedCopy_DoesNotDeleteHyphenatedSiblingAssemblyCaches, for
        // one, deletes the copy and then checks that this call writes it and deletes the stale
        // ones. If the installed warm-up writes the copy in between, this call is a cache hit and
        // the check cannot be made.
        [UnitySetUp]
        public IEnumerator WaitForTheInstalledWarmUpToStop()
        {
            Task stopped = PublicizedCopyTestCache.StopInstalledWarmUpAsync();
            while (!stopped.IsCompleted)
            {
                yield return null;
            }
        }

        /// <summary>
        /// What: publicizing the test assembly exposes private fields/methods and reuses the
        /// cached copy on a second call (same path; last-write time stays at a planted marker).
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_RewritesPrivateMembersAndCachesByMvid()
        {
            HotReloadTypeHome home = ResolveTestAssemblyHome();
            IReadOnlyCollection<string> searchDirectories = PublicizerTestSearchDirectories.ForHotReloadTestAssembly();
            string firstPath = ReferencePublicizer.GetOrCreatePublicizedCopy(home, searchDirectories);

            // Plant a distinctive mtime: a cache hit must leave it alone, while a regenerate
            // would rewrite the file and replace this marker with "now". Avoids Thread.Sleep
            // (banned on the EditMode main thread) while still proving the early-return path.
            DateTime markerWriteTimeUtc = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(firstPath, markerWriteTimeUtc);

            string secondPath = ReferencePublicizer.GetOrCreatePublicizedCopy(home, searchDirectories);

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
        /// What: a 0-byte file at the publicized cache path is rejected and regenerated instead
        /// of being treated as a valid hit (recovers from failed Write pollution).
        /// </summary>
        [Test]
        public void GetOrCreatePublicizedCopy_RegeneratesWhenCachedDllIsEmpty()
        {
            HotReloadTypeHome home = ResolveTestAssemblyHome();
            IReadOnlyCollection<string> searchDirectories = PublicizerTestSearchDirectories.ForHotReloadTestAssembly();
            string outputPath = ReferencePublicizer.GetOrCreatePublicizedCopy(home, searchDirectories);
            Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0), "Precondition: first publicize must write bytes.");

            File.Delete(outputPath);
            File.WriteAllBytes(outputPath, Array.Empty<byte>());
            Assert.That(new FileInfo(outputPath).Length, Is.EqualTo(0), "Precondition: planted cache must be empty.");

            string regeneratedPath = ReferencePublicizer.GetOrCreatePublicizedCopy(home, searchDirectories);
            Assert.That(regeneratedPath, Is.EqualTo(outputPath), "Regeneration must target the same Mvid cache path.");
            Assert.That(
                new FileInfo(regeneratedPath).Length,
                Is.GreaterThan(0),
                "Empty cached DLL must be replaced with a non-empty publicized copy.");
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
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);

            string siblingCachePath = Path.Combine(
                outputDirectory,
                TestAssemblyName + "-Sibling-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(siblingCachePath, new byte[] { 0x4D, 0x5A });

            string staleSameAssemblyPath = Path.Combine(
                outputDirectory,
                TestAssemblyName + "-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(staleSameAssemblyPath, new byte[] { 0x4D, 0x5A });

            string publicizedPath = ReferencePublicizer.GetOrCreatePublicizedCopy(
                ResolveTestAssemblyHome(),
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
                ResolveTestAssemblyHome(),
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

        /// <summary>
        /// What: an introduced-type artifact this domain retains is publicized as well, so a shim
        /// compiled against it can bind the private members of a type that lives there instead of
        /// under Library/ScriptAssemblies.
        /// </summary>
        [Test]
        public async Task GetOrCreatePublicizedCopy_PublicizesARetainedIntroducedTypeArtifact()
        {
            HotReloadTypeHome home = await CompileAndLoadIntroducedTypeArtifactAsync();

            string publicizedPath = ReferencePublicizer.GetOrCreatePublicizedCopy(
                home,
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly());

            Assert.That(File.Exists(publicizedPath), Is.True, "Publicized artifact copy must be written.");
            Assert.That(
                new FileInfo(publicizedPath).Length,
                Is.GreaterThan(0),
                "Publicized artifact copy must not be empty.");

            using AssemblyDefinition publicizedAssembly = AssemblyDefinition.ReadAssembly(publicizedPath);
            TypeDefinition introducedType = publicizedAssembly.MainModule.GetType(ArtifactIntroducedTypeMetadataName);
            Assert.That(introducedType, Is.Not.Null, $"Type not found: {ArtifactIntroducedTypeMetadataName}");

            FieldDefinition counterField = introducedType.Fields.First(field => field.Name == "_counter");
            Assert.That(
                (counterField.Attributes & CecilFieldAttributes.FieldAccessMask),
                Is.EqualTo(CecilFieldAttributes.Public),
                "A private field of the artifact type must be public after publicize.");

            MethodDefinition secretMethod = introducedType.Methods.First(method => method.Name == "Secret");
            Assert.That(
                (secretMethod.Attributes & CecilMethodAttributes.MemberAccessMask),
                Is.EqualTo(CecilMethodAttributes.Public),
                "A private method of the artifact type must be public after publicize.");
        }

        // Why a real compile and load: the publicizer reads the image from disk and decides on
        // where it sits, so a dynamic assembly with no file would not exercise the decision, and
        // a retained-artifact home has to name the assembly this domain actually loaded.
        private static async Task<HotReloadTypeHome> CompileAndLoadIntroducedTypeArtifactAsync()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string artifactDirectoryPath = Path.Combine(
                projectRootPath,
                "Library",
                "UloopHotReload",
                "IntroducedTypes",
                "publicizer-fixture");
            Directory.CreateDirectory(artifactDirectoryPath);

            string sourcePath = Path.Combine(artifactDirectoryPath, ArtifactAssemblyName + ".cs");
            File.WriteAllText(sourcePath, ArtifactIntroducedTypeSource);
            string dllPath = Path.Combine(artifactDirectoryPath, ArtifactAssemblyName + ".dll");

            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(
                externalCompilerPaths,
                Is.Not.Null,
                "External compiler paths could not be resolved for this Unity installation.");

            RoslynCompilerOptions compilerOptions = new RoslynCompilerOptions(
                new List<string>(),
                false,
                emitDebugCode: false);
            using CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                sourcePath,
                dllPath,
                new List<string> { typeof(object).Assembly.Location },
                externalCompilerPaths,
                compilerOptions,
                cancellation.Token,
                () => { },
                () => { },
                () => { });

            AssertNoArtifactCompileErrors(result.CompilerMessages);
            Assert.That(File.Exists(dllPath), Is.True, $"Artifact dll was not produced: {dllPath}");

            ReflectionAssembly artifactAssembly = ReflectionAssembly.Load(File.ReadAllBytes(dllPath));
            return HotReloadTypeHome.RetainedArtifact(ArtifactAssemblyName, dllPath, artifactAssembly);
        }

        private static void AssertNoArtifactCompileErrors(CompilerMessage[] compilerMessages)
        {
            List<string> errors = new List<string>();
            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error)
                {
                    errors.Add(compilerMessage.message);
                }
            }

            Assert.That(errors, Is.Empty, "Artifact compilation failed:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// What: a shim reference copy built for a target the assembly grants no internals to keeps
        /// a top-level internal type, and the private, internal and private protected members of
        /// public types, as they are, while a protected member and a nested type are publicized as
        /// before.
        /// </summary>
        [Test]
        public void GetOrCreateShimReferenceCopy_WhenTheAssemblyDoesNotGrantInternalsToTheTarget_KeepsInternalTypesAndMembersHidden()
        {
            using AssemblyDefinition copy = ReadFreshShimReferenceCopyOfTestAssembly(StrangerTargetAssemblyName);

            Assert.That(
                FindType(copy, HiddenExtensionsTypeFullName).Attributes & CecilTypeAttributes.VisibilityMask,
                Is.EqualTo(CecilTypeAttributes.NotPublic),
                "A top-level internal type must stay internal: the target's own compile never saw it.");

            TypeDefinition publicHostType = FindType(copy, PublicHostTypeFullName);
            Assert.That(
                MethodAccessOf(publicHostType, "Hidden"),
                Is.EqualTo(CecilMethodAttributes.Assembly),
                "An internal member of a public type must stay internal: the target's own compile never saw it.");
            Assert.That(
                publicHostType.Fields.First(field => field.Name == "HiddenField").Attributes & CecilFieldAttributes.FieldAccessMask,
                Is.EqualTo(CecilFieldAttributes.Assembly),
                "An internal field of a public type must stay internal: the target's own compile never saw it.");
            TypeDefinition publicBaseType = FindType(copy, PublicBaseTypeFullName);
            Assert.That(
                MethodAccessOf(publicBaseType, "Guarded"),
                Is.EqualTo(CecilMethodAttributes.FamANDAssem),
                "A private protected member must stay as it is: another assembly reaches it only through a grant.");
            Assert.That(
                MethodAccessOf(publicHostType, "Secret"),
                Is.EqualTo(CecilMethodAttributes.Private),
                "A private member must stay private: no other assembly's compile sees it.");
            Assert.That(
                publicHostType.Fields.First(field => field.Name == "SecretField").Attributes & CecilFieldAttributes.FieldAccessMask,
                Is.EqualTo(CecilFieldAttributes.Private),
                "A private field must stay private: no other assembly's compile sees it.");

            Assert.That(
                MethodAccessOf(publicBaseType, "Shielded"),
                Is.EqualTo(CecilMethodAttributes.Public),
                "A protected member must still be publicized: a shim calls it from outside the type hierarchy.");

            TypeDefinition nestedInternalType = publicHostType.NestedTypes.First(type => type.Name == "NestedInternal");
            Assert.That(
                nestedInternalType.Attributes & CecilTypeAttributes.VisibilityMask,
                Is.EqualTo(CecilTypeAttributes.NestedPublic),
                "A nested type must still be publicized.");
        }

        /// <summary>
        /// What: a shim reference copy built for a target the assembly grants internals to
        /// publicizes an internal member of a public type, as every shim reference copy did before.
        /// </summary>
        [Test]
        public void GetOrCreateShimReferenceCopy_WhenTheAssemblyGrantsInternalsToTheTarget_PublicizesInternalMembers()
        {
            using AssemblyDefinition copy = ReadFreshShimReferenceCopyOfTestAssembly(FriendTargetAssemblyName);

            Assert.That(
                MethodAccessOf(FindType(copy, PublicHostTypeFullName), "Hidden"),
                Is.EqualTo(CecilMethodAttributes.Public),
                "An internal member must be publicized for a target the assembly grants internals to.");
        }

        /// <summary>
        /// What: a shim reference copy built for a target the assembly grants internals to
        /// publicizes a top-level internal type, as every shim reference copy did before.
        /// </summary>
        [Test]
        public void GetOrCreateShimReferenceCopy_WhenTheAssemblyGrantsInternalsToTheTarget_PublicizesInternalTopLevelTypes()
        {
            using AssemblyDefinition copy = ReadFreshShimReferenceCopyOfTestAssembly(FriendTargetAssemblyName);

            Assert.That(
                FindType(copy, HiddenExtensionsTypeFullName).Attributes & CecilTypeAttributes.VisibilityMask,
                Is.EqualTo(CecilTypeAttributes.Public),
                "A top-level internal type must be publicized for a target the assembly grants internals to.");
        }

        /// <summary>
        /// What: the copy that keeps internals hidden and the fully publicized copy of the same image
        /// are cached in different directories, so neither is returned in place of the other.
        /// </summary>
        [Test]
        public void GetOrCreateShimReferenceCopy_WritesTheTwoVariantsToDifferentDirectories()
        {
            HotReloadTypeHome home = ResolveTestAssemblyHome();
            IReadOnlyCollection<string> searchDirectories = PublicizerTestSearchDirectories.ForHotReloadTestAssembly();

            string strangerCopyPath = ReferencePublicizer.GetOrCreateShimReferenceCopy(
                home,
                searchDirectories,
                StrangerTargetAssemblyName);
            string friendCopyPath = ReferencePublicizer.GetOrCreateShimReferenceCopy(
                home,
                searchDirectories,
                FriendTargetAssemblyName);

            Assert.That(strangerCopyPath, Is.Not.EqualTo(friendCopyPath), "The two variants must not share a path.");
            Assert.That(File.Exists(strangerCopyPath), Is.True, "The copy that keeps internals hidden must exist.");
            Assert.That(File.Exists(friendCopyPath), Is.True, "The fully publicized copy must exist.");
            Assert.That(
                NormalizedDirectoryOf(strangerCopyPath),
                Is.EqualTo(NormalizedProjectDirectory(HotReloadConstants.PublicizedExternalRefsRelativeDirectory)),
                "The copy that keeps internals hidden must be cached under PublicizedExternalRefs.");
            Assert.That(
                NormalizedDirectoryOf(friendCopyPath),
                Is.EqualTo(NormalizedProjectDirectory(HotReloadConstants.PublicizedRefsRelativeDirectory)),
                "The fully publicized copy must be cached under PublicizedRefs.");
        }

        /// <summary>
        /// What: the friend name of an InternalsVisibleTo argument is the text before the first
        /// comma without surrounding spaces, so a grant with a public key still names its assembly.
        /// </summary>
        [Test]
        public void ParseFriendAssemblyName_StripsThePublicKeySuffix()
        {
            Assert.That(
                ReferencePublicizer.ParseFriendAssemblyName("Friend, PublicKey=0024000004800000"),
                Is.EqualTo("Friend"));
            Assert.That(ReferencePublicizer.ParseFriendAssemblyName(" Friend "), Is.EqualTo("Friend"));
            Assert.That(ReferencePublicizer.ParseFriendAssemblyName("Friend"), Is.EqualTo("Friend"));
        }

        /// <summary>
        /// What: a grant matches the target assembly name regardless of case, the way the compiler
        /// matches assembly simple names.
        /// </summary>
        [Test]
        public void GetOrCreateShimReferenceCopy_MatchesTheTargetNameIgnoringCase()
        {
            using AssemblyDefinition copy = ReadFreshShimReferenceCopyOfTestAssembly("uloopshimreferencefriendtarget");

            Assert.That(
                FindType(copy, HiddenExtensionsTypeFullName).Attributes & CecilTypeAttributes.VisibilityMask,
                Is.EqualTo(CecilTypeAttributes.Public),
                "A grant whose name differs only in case must still publicize internal types.");
        }

        // Why delete the cached copies of both variants first: a copy is reused by assembly name and
        // Mvid alone, so one that an earlier test or run left behind would pass without the rewrite
        // under test running.
        private static AssemblyDefinition ReadFreshShimReferenceCopyOfTestAssembly(string shimTargetAssemblyName)
        {
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedRefsRelativeDirectory);
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName, HotReloadConstants.PublicizedExternalRefsRelativeDirectory);

            string copyPath = ReferencePublicizer.GetOrCreateShimReferenceCopy(
                ResolveTestAssemblyHome(),
                PublicizerTestSearchDirectories.ForHotReloadTestAssembly(),
                shimTargetAssemblyName);
            return AssemblyDefinition.ReadAssembly(copyPath);
        }

        private static TypeDefinition FindType(AssemblyDefinition assemblyDefinition, string fullName)
        {
            TypeDefinition type = assemblyDefinition.MainModule.GetType(fullName);
            Assert.That(type, Is.Not.Null, $"Type not found: {fullName}");
            return type;
        }

        private static CecilMethodAttributes MethodAccessOf(TypeDefinition type, string methodName)
        {
            MethodDefinition method = type.Methods.First(candidate => candidate.Name == methodName);
            return method.Attributes & CecilMethodAttributes.MemberAccessMask;
        }

        private static string NormalizedDirectoryOf(string filePath)
        {
            return Path.GetDirectoryName(Path.GetFullPath(filePath)).Replace('\\', '/');
        }

        private static string NormalizedProjectDirectory(string relativeDirectory)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRootPath, relativeDirectory)).Replace('\\', '/');
        }

        private static HotReloadTypeHome ResolveTestAssemblyHome()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssembliesUnderProject(projectRootPath, TestAssemblyName);
            Assert.That(File.Exists(home.DllPath), Is.True, $"Test assembly dll not found: {home.DllPath}");
            return home;
        }
    }
}
