using System;
using System.IO;
using System.Reflection;

using Mono.Cecil;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadMethodMatcher"/> resolution and overload selection,
    /// and for how often one matcher reads a compiled image.
    /// </summary>
    public class HotReloadMethodMatcherTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCoreFixture";
        private const string SameNameFixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadPeelSameNameFixture";
        // Cecil names a nested type with '/', the separator the worker's rows carry.
        private const string NestedFixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadBindingSplitNestedRegistry/Inner";
        private const string DomainTypeMetadataName = "io.github.hatayama.UnityCliLoop.FirstPartyTools.HotReloadDomain";
        private const string NotLoadedAssemblyName = "NotLoaded.Assembly.ForTest";

        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-method-matcher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        // The test assembly is itself a project assembly, so its compiled image is the one the
        // matcher reads: a ScriptAssemblies home naming it is what production passes in.
        private static HotReloadTypeHome TestAssemblyHome =>
            HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, TestAssemblyDllPath);

        private static string TestAssemblyDllPath
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, "Library/ScriptAssemblies", TestAssemblyName + ".dll");
            }
        }

        // The matcher's own assembly, Patching, is a second project assembly with an image of its
        // own, so an entry whose home names it must be answered from that image and not from the
        // test assembly's.
        private static HotReloadTypeHome PatchingAssemblyHome
        {
            get
            {
                Assembly patchingAssembly = typeof(HotReloadMethodMatcher).Assembly;
                return HotReloadTypeHome.ScriptAssemblies(patchingAssembly.GetName().Name, patchingAssembly.Location);
            }
        }

        private static MethodInfo StaticPingMethod =>
            typeof(HotReloadCoreFixture).GetMethod(nameof(HotReloadCoreFixture.StaticPing));

        private static MethodInfo AddMethod =>
            typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.Add),
                new[] { typeof(int), typeof(int) });

        private string TempImagePath => Path.Combine(_tempDirectory, TestAssemblyName + ".dll");

        /// <summary>
        /// What: a known instance method resolves to the live MethodBase with matching MetadataToken identity.
        /// </summary>
        [Test]
        public void Resolve_KnownInstanceMethod_ReturnsLiveMethodBase()
        {
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult result = matcher.Resolve(
                TestAssemblyHome,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.Int32", "System.Int32" },
                0);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method, Is.Not.Null);
            Assert.That(result.Method.Name, Is.EqualTo(nameof(HotReloadCoreFixture.Add)));
            Assert.That(result.Method.GetParameters().Length, Is.EqualTo(2));

            MethodInfo expected = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.Add),
                new[] { typeof(int), typeof(int) });
            Assert.That(result.Method, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: overload selection uses parameter type full names, picking the three-int Add.
        /// </summary>
        [Test]
        public void Resolve_Overload_SelectsMatchingParameterTypes()
        {
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult result = matcher.Resolve(
                TestAssemblyHome,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.Int32", "System.Int32", "System.Int32" },
                0);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method.GetParameters().Length, Is.EqualTo(3));

            MethodInfo expected = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.Add),
                new[] { typeof(int), typeof(int), typeof(int) });
            Assert.That(result.Method, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: a parameter-type mismatch yields MethodNotFound rather than a wrong overload.
        /// </summary>
        [Test]
        public void Resolve_ParameterTypeMismatch_ReturnsMethodNotFound()
        {
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult result = matcher.Resolve(
                TestAssemblyHome,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.String", "System.Int32" },
                0);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.MethodNotFound));
        }

        /// <summary>
        /// What: a type the compiled image does not declare yields TypeNotFound.
        /// </summary>
        [Test]
        public void Resolve_UnknownType_ReturnsTypeNotFound()
        {
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult result = matcher.Resolve(
                TestAssemblyHome,
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.NoSuchFixtureType",
                nameof(HotReloadCoreFixture.StaticPing),
                new string[0],
                0);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.TypeNotFound));
        }

        /// <summary>
        /// What: a static method with no parameters resolves correctly.
        /// </summary>
        [Test]
        public void Resolve_StaticMethod_ReturnsLiveMethodBase()
        {
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult result = matcher.Resolve(
                TestAssemblyHome,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.StaticPing),
                new string[0],
                0);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Method.IsStatic, Is.True);
            Assert.That(result.Method, Is.EqualTo(typeof(HotReloadCoreFixture).GetMethod(nameof(HotReloadCoreFixture.StaticPing))));
        }

        /// <summary>
        /// What: Resolve distinguishes Caller(int) from Caller&lt;T&gt;(int) by generic arity
        /// so an unchanged generic row cannot resolve to the non-generic sibling.
        /// </summary>
        [Test]
        public void Resolve_SameNameAndParameters_SelectsByGenericArity()
        {
            const string typeMetadataName =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeGenericCallerFixture";
            string[] parameterTypeFullNames = { "System.Int32" };
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();

            HotReloadMethodMatchResult nonGeneric = matcher.Resolve(
                TestAssemblyHome,
                typeMetadataName,
                nameof(HotReloadSignatureChangeGenericCallerFixture.Caller),
                parameterTypeFullNames,
                0);
            HotReloadMethodMatchResult generic = matcher.Resolve(
                TestAssemblyHome,
                typeMetadataName,
                nameof(HotReloadSignatureChangeGenericCallerFixture.Caller),
                parameterTypeFullNames,
                1);

            Assert.That(nonGeneric.Success, Is.True, nonGeneric.ErrorMessage);
            Assert.That(generic.Success, Is.True, generic.ErrorMessage);

            MethodInfo nonGenericMethod = (MethodInfo)nonGeneric.Method;
            MethodInfo genericMethod = (MethodInfo)generic.Method;
            Assert.That(nonGenericMethod.IsGenericMethod, Is.False);
            Assert.That(genericMethod.IsGenericMethodDefinition, Is.True);
            Assert.That(genericMethod.GetGenericArguments().Length, Is.EqualTo(1));
            Assert.That(nonGeneric.Method, Is.Not.EqualTo(generic.Method));
        }

        /// <summary>
        /// What: a mismatched Mvid against a loaded assembly fails with StaleAssembly without
        /// resolving a method from a stale token.
        /// </summary>
        [Test]
        public void ResolveLoadedMethod_MvidMismatch_ReturnsStaleAssembly()
        {
            MethodInfo knownMethod = typeof(HotReloadCoreFixture).GetMethod(
                nameof(HotReloadCoreFixture.StaticPing));
            int metadataToken = knownMethod.MetadataToken;

            HotReloadMethodMatchResult result = HotReloadMethodMatcher.ResolveLoadedMethod(
                TestAssemblyHome,
                "00000000-0000-0000-0000-000000000000",
                metadataToken);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.StaleAssembly));
            Assert.That(result.Method, Is.Null);
        }

        /// <summary>
        /// What: entries of one compiled image, on two types and one of them nested, are all
        /// answered from a single read, each with the live method reflection finds.
        /// </summary>
        [Test]
        public void Resolve_SeveralEntriesOfOneImage_ReadsTheImageOnce()
        {
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            HotReloadMethodMatchResult add = ResolveAdd(matcher, TestAssemblyHome);
            HotReloadMethodMatchResult otherType = matcher.Resolve(
                TestAssemblyHome,
                SameNameFixtureTypeMetadataName,
                nameof(HotReloadPeelSameNameFixture.StaticPing),
                new string[0],
                0);
            HotReloadMethodMatchResult nested = matcher.Resolve(
                TestAssemblyHome,
                NestedFixtureTypeMetadataName,
                nameof(HotReloadBindingSplitNestedRegistry.Inner.Raise),
                new[] { "System.Int32" },
                0);

            AssertResolvedTo(add, AddMethod);
            AssertResolvedTo(
                otherType,
                typeof(HotReloadPeelSameNameFixture).GetMethod(nameof(HotReloadPeelSameNameFixture.StaticPing)));
            AssertResolvedTo(
                nested,
                typeof(HotReloadBindingSplitNestedRegistry.Inner).GetMethod(
                    nameof(HotReloadBindingSplitNestedRegistry.Inner.Raise)));
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a run that resolves entries of two compiled images, alternating between them,
        /// reads each image once and answers every entry from the image its home names.
        /// </summary>
        [Test]
        public void Resolve_TwoImages_ReadsEachOnceAndAnswersFromItsOwn()
        {
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);
            MethodInfo domainDispose = typeof(HotReloadDomain).GetMethod(nameof(HotReloadDomain.Dispose));

            for (int round = 0; round < 2; round++)
            {
                AssertResolvedTo(ResolveStaticPing(matcher, TestAssemblyHome), StaticPingMethod);
                HotReloadMethodMatchResult fromPatchingImage = matcher.Resolve(
                    PatchingAssemblyHome,
                    DomainTypeMetadataName,
                    nameof(HotReloadDomain.Dispose),
                    new string[0],
                    0);
                AssertResolvedTo(fromPatchingImage, domainDispose);
            }

            Assert.That(loader.Loads, Is.EqualTo(2));
        }

        /// <summary>
        /// What: an image that is not there yet is reported missing without being remembered, so
        /// once the file appears the same matcher reads it and resolves the next entry.
        /// </summary>
        [Test]
        public void Resolve_MissingImage_IsLookedForAgainOnTheNextEntry()
        {
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, TempImagePath);
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            HotReloadMethodMatchResult missing = ResolveStaticPing(matcher, home);
            Assert.That(missing.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.CompiledAssemblyNotFound));
            Assert.That(loader.Loads, Is.EqualTo(0));

            File.Copy(TestAssemblyDllPath, TempImagePath);
            AssertResolvedTo(ResolveStaticPing(matcher, home), StaticPingMethod);
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a read that throws reaches the caller and leaves nothing behind, so the next entry
        /// of the same matcher reads the image again and resolves.
        /// </summary>
        [Test]
        public void Resolve_ImageThatFailedToRead_IsReadAgainOnTheNextEntry()
        {
            CountingLoader loader = new CountingLoader { FailsReads = true };
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            Assert.Throws<IOException>(() => ResolveStaticPing(matcher, TestAssemblyHome));

            loader.FailsReads = false;
            AssertResolvedTo(ResolveStaticPing(matcher, TestAssemblyHome), StaticPingMethod);
            Assert.That(loader.Loads, Is.EqualTo(2));
        }

        /// <summary>
        /// What: once a matcher has read an image, deleting the file does not change its answers:
        /// a later entry of the same run still resolves from that read.
        /// </summary>
        [Test]
        public void Resolve_ImageDeletedAfterTheFirstRead_StillAnswersFromThatRead()
        {
            HotReloadTypeHome home = CopyTestAssemblyHome();
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);
            AssertResolvedTo(ResolveStaticPing(matcher, home), StaticPingMethod);

            File.Delete(home.DllPath);

            AssertResolvedTo(ResolveAdd(matcher, home), AddMethod);
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a matcher keeps answering from the image it read after the file is replaced by
        /// another image, while a new matcher, as the next run makes, reads the replacement.
        /// </summary>
        [Test]
        public void Resolve_ImageReplacedAfterTheFirstRead_SameMatcherKeepsItsRead_AndANewMatcherReadsTheNewImage()
        {
            HotReloadTypeHome home = CopyTestAssemblyHome();
            using HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();
            AssertResolvedTo(ResolveStaticPing(matcher, home), StaticPingMethod);

            File.Copy(typeof(HotReloadMethodMatcher).Assembly.Location, home.DllPath, overwrite: true);

            AssertResolvedTo(ResolveAdd(matcher, home), AddMethod);
            using HotReloadMethodMatcher nextRunMatcher = HotReloadMethodMatcher.CreateReadingFromDisk();
            HotReloadMethodMatchResult fromReplacement = ResolveAdd(nextRunMatcher, home);
            Assert.That(fromReplacement.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.TypeNotFound));
        }

        /// <summary>
        /// What: disposing a matcher twice is harmless, and a disposed matcher refuses to resolve
        /// instead of answering from a read it has released.
        /// </summary>
        [Test]
        public void Resolve_AfterDispose_Throws()
        {
            HotReloadMethodMatcher matcher = HotReloadMethodMatcher.CreateReadingFromDisk();
            AssertResolvedTo(ResolveStaticPing(matcher, TestAssemblyHome), StaticPingMethod);

            matcher.Dispose();
            Assert.DoesNotThrow(matcher.Dispose);

            Assert.Throws<ObjectDisposedException>(() => ResolveStaticPing(matcher, TestAssemblyHome));
        }

        /// <summary>
        /// What: an image whose assembly is not loaded reports AssemblyNotLoaded for every entry,
        /// because the loaded-assembly check runs per entry, while the image itself is read once.
        /// </summary>
        [Test]
        public void Resolve_AssemblyNotLoaded_IsReportedForEveryEntry_WithOneRead()
        {
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(NotLoadedAssemblyName, CopyTestAssembly());
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            HotReloadMethodMatchResult first = ResolveStaticPing(matcher, home);
            HotReloadMethodMatchResult second = ResolveStaticPing(matcher, home);

            Assert.That(first.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.AssemblyNotLoaded));
            Assert.That(second.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.AssemblyNotLoaded));
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        /// <summary>
        /// What: two homes that name the same image under different assembly names share one read,
        /// but each is checked against the loaded assemblies on its own, so the home whose assembly
        /// is not loaded reports AssemblyNotLoaded after the other home resolved.
        /// </summary>
        [Test]
        public void Resolve_SameImageUnderTwoHomes_ChecksTheLoadedAssemblyForEachHome()
        {
            HotReloadTypeHome notLoadedHome =
                HotReloadTypeHome.ScriptAssemblies(NotLoadedAssemblyName, TestAssemblyDllPath);
            CountingLoader loader = new CountingLoader();
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            AssertResolvedTo(ResolveStaticPing(matcher, TestAssemblyHome), StaticPingMethod);
            HotReloadMethodMatchResult underNotLoadedName = ResolveStaticPing(matcher, notLoadedHome);

            Assert.That(underNotLoadedName.FailureReason, Is.EqualTo(HotReloadMethodMatchFailureReason.AssemblyNotLoaded));
            Assert.That(loader.Loads, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a loader that returns no image breaks its contract, so the matcher throws instead
        /// of resolving against nothing, and remembers nothing: the next entry reads the image again.
        /// </summary>
        [Test]
        public void Resolve_LoaderReturningNull_Throws_AndTheImageIsReadAgainOnTheNextEntry()
        {
            CountingLoader loader = new CountingLoader { ReturnsNoImage = true };
            using HotReloadMethodMatcher matcher = new HotReloadMethodMatcher(loader.Load);

            Assert.Throws<InvalidOperationException>(() => ResolveStaticPing(matcher, TestAssemblyHome));

            loader.ReturnsNoImage = false;
            AssertResolvedTo(ResolveStaticPing(matcher, TestAssemblyHome), StaticPingMethod);
            Assert.That(loader.Loads, Is.EqualTo(2));
        }

        private static HotReloadMethodMatchResult ResolveStaticPing(HotReloadMethodMatcher matcher, HotReloadTypeHome home)
        {
            return matcher.Resolve(
                home,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.StaticPing),
                new string[0],
                0);
        }

        private static HotReloadMethodMatchResult ResolveAdd(HotReloadMethodMatcher matcher, HotReloadTypeHome home)
        {
            return matcher.Resolve(
                home,
                FixtureTypeMetadataName,
                nameof(HotReloadCoreFixture.Add),
                new[] { "System.Int32", "System.Int32" },
                0);
        }

        private static void AssertResolvedTo(HotReloadMethodMatchResult result, MethodInfo expected)
        {
            Assert.That(expected, Is.Not.Null, "Reflection must find the method the entry names.");
            Assert.That(result.Success, Is.True, result.FailureReason + ": " + result.ErrorMessage);
            Assert.That(result.Method, Is.EqualTo(expected));
        }

        // A copy of the test assembly's image keeps the loaded assembly's Mvid, so it resolves
        // like the original while the test is free to delete or replace the file.
        private HotReloadTypeHome CopyTestAssemblyHome()
        {
            return HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, CopyTestAssembly());
        }

        private string CopyTestAssembly()
        {
            File.Copy(TestAssemblyDllPath, TempImagePath);
            return TempImagePath;
        }

        /// <summary>
        /// A loader that counts every read it is asked for, failed ones included, and can be told
        /// to fail them or to return no image.
        /// </summary>
        private sealed class CountingLoader
        {
            public int Loads { get; private set; }

            public bool FailsReads { get; set; }

            public bool ReturnsNoImage { get; set; }

            public AssemblyDefinition Load(string dllPath)
            {
                Loads++;
                if (FailsReads)
                {
                    throw new IOException("Simulated read failure: " + dllPath);
                }

                if (ReturnsNoImage)
                {
                    return null;
                }

                return HotReloadMethodMatcher.ReadCompiledAssembly(dllPath);
            }
        }
    }
}
