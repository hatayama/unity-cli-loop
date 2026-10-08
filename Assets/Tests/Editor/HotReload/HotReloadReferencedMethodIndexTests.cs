using System;
using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the per-assembly referenced-method index: which methods of other assemblies a
    /// dll's MemberRef table names, and when the dll is read again. A test that changes a dll works
    /// on a copy in a private temp directory so that ScriptAssemblies is never touched.
    /// </summary>
    public class HotReloadReferencedMethodIndexTests
    {
        private const string TestScriptProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadReferencedMethodIndexTests.cs";

        private const string CrossAssemblyCallerAssemblyName =
            "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerFixture";

        private const string CrossAssemblyTargetTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCallSiteScannerCrossAssemblyTarget";

        private const string CrossAssemblyGenericHostTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossAssemblyGenericHost`1";

        private string _temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "uloop-referenced-method-index-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }

        /// <summary>
        /// What: a dll that calls a method of another assembly names that method.
        /// </summary>
        [Test]
        public void MentionsAny_MethodOfAnotherAssemblyTheDllCalls_IsTrue()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") });

            Assert.That(mentioned, Is.True);
        }

        /// <summary>
        /// What: a method no other assembly calls is not named by the referencing dll.
        /// </summary>
        [Test]
        public void MentionsAny_MethodNobodyCalls_IsFalse()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), FixtureTypeMetadataName, "NeverCalled") });

            Assert.That(mentioned, Is.False);
        }

        /// <summary>
        /// What: a call through a constructed generic type is filed under the open type's name.
        /// </summary>
        [Test]
        public void MentionsAny_CallThroughConstructedGenericType_IsFiledUnderTheOpenType()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();

            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyGenericHostTypeMetadataName, "Target") });

            Assert.That(mentioned, Is.True);
        }

        /// <summary>
        /// What: a method of a nested type is filed under the metadata name with '/', not the
        /// reflection or source spelling.
        /// </summary>
        [Test]
        public void MentionsAny_NestedTypeKey_UsesTheMetadataSeparator()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();
            const string methodName = "CalledFromOtherAssembly";

            bool metadataSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + "/Nested", methodName) });
            bool reflectionSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + "+Nested", methodName) });
            bool sourceSpelling = index.MentionsAny(
                CrossDll(),
                new[] { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName + ".Nested", methodName) });

            Assert.That(metadataSpelling, Is.True);
            Assert.That(reflectionSpelling, Is.False);
            Assert.That(sourceSpelling, Is.False);
        }

        /// <summary>
        /// What: a method of the dll's own module that the dll calls is not named, because that call
        /// is a MethodDef operand with no MemberRef row.
        /// </summary>
        [Test]
        public void MentionsAny_OwnMethodDefCallee_IsFalse()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();

            // The cross assembly defines its own type with the fixture's full name, and its
            // CallSameFullNameTarget calls this method.
            bool mentioned = index.MentionsAny(
                CrossDll(),
                new[] { Key(CrossAssemblyCallerAssemblyName, FixtureTypeMetadataName, "CalledFromCrossAssembly") });

            Assert.That(mentioned, Is.False);
        }

        /// <summary>
        /// What: a second question about an unchanged dll is answered without reading it again.
        /// </summary>
        [Test]
        public void MentionsAny_SameDllTwice_ReadsOnce()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();
            string[] keys = { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") };

            index.MentionsAny(CrossDll(), keys);
            index.MentionsAny(CrossDll(), keys);

            Assert.That(index.LoadCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a dll whose write time changed after it was read is read again.
        /// </summary>
        [Test]
        public void MentionsAny_DllWriteTimeChanged_ReadsAgain()
        {
            HotReloadReferencedMethodIndex index = new HotReloadReferencedMethodIndex();
            string copy = CopyCrossDllToTemp();
            string[] keys = { Key(TestAssemblyName(), CrossAssemblyTargetTypeMetadataName, "Called") };

            bool first = index.MentionsAny(copy, keys);
            File.SetLastWriteTimeUtc(copy, File.GetLastWriteTimeUtc(copy).AddMinutes(1));
            bool second = index.MentionsAny(copy, keys);

            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(index.LoadCount, Is.EqualTo(2));
        }

        private static string Key(string assemblyName, string openDeclaringTypeFullName, string methodName)
        {
            return HotReloadReferencedMethodIndex.BuildKey(assemblyName, openDeclaringTypeFullName, methodName);
        }

        private string CopyCrossDllToTemp()
        {
            string sourceDllPath = CrossDll();
            Assert.That(File.Exists(sourceDllPath), Is.True, "Assembly dll missing: " + sourceDllPath);

            Directory.CreateDirectory(_temporaryDirectory);
            string copy = Path.Combine(_temporaryDirectory, Path.GetFileName(sourceDllPath));
            File.Copy(sourceDllPath, copy);
            return copy;
        }

        private static string CrossDll()
        {
            return DllPath(CrossAssemblyCallerAssemblyName);
        }

        private static string DllPath(string assemblyName)
        {
            return Path.Combine(
                ProjectRoot(),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string TestAssemblyName()
        {
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(
                TestScriptProjectRelativePath);
            return Path.GetFileNameWithoutExtension(rawAssemblyName);
        }
    }
}
